// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Common.Rotation;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// Purity repair on the finished plan: exchanges a run of same-kind shifts of one agent with the shifts another agent works on
/// exactly those days when that makes their blocks purer. Both agents keep every worked day, so the package structure stays as it was. A swap is
/// accepted only in the spec's order (SPEC-ROTATION-2026-10-08 rule 4): the two agents' non-ideal block changes may not
/// rise, their kind changes inside blocks must fall, and the lexicographic fitness must improve strictly, so hard rules,
/// coverage, hours and rotation are never traded for purity. Locked tokens, split-duty days and days that continue an
/// open carry-in package are never touched. Deterministic: agents in roster order, days ascending, first improvement wins.
/// </summary>
public sealed class PurityBalancer
{
    /// <summary>Upper bound on accepted swaps per invocation; a safety net, not a tuning knob.</summary>
    private const int MaxSwaps = 96;

    /// <summary>
    /// Returns a strictly better plan produced by same-day purity swaps, or the unchanged input.
    /// </summary>
    /// <param name="scenario">Plan to repair; its tokens are never modified, its fitness fields are refreshed</param>
    /// <param name="context">Wizard context supplying the roster, the rules and the ban list</param>
    /// <param name="evaluator">Fitness evaluator used as the acceptance gate</param>
    /// <param name="cancellationToken">Stops the repair between candidates and keeps the best plan reached so far</param>
    public CoreScenario Apply(
        CoreScenario scenario,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
        CancellationToken cancellationToken = default)
    {
        if (context.Agents.Count < 2 || scenario.Tokens.Count == 0)
        {
            return scenario;
        }

        var rotation = RotationContext.For(context);
        var carryInDays = CarryInDays(context);
        var current = scenario;
        evaluator.Evaluate(current, context);
        for (var i = 0; i < MaxSwaps && !cancellationToken.IsCancellationRequested; i++)
        {
            var swapped = FindImprovingSwap(current, context, evaluator, rotation, carryInDays, cancellationToken);
            if (swapped is null)
            {
                break;
            }

            current = swapped;
        }

        return current;
    }

    private static CoreScenario? FindImprovingSwap(
        CoreScenario scenario,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
        RotationContext rotation,
        HashSet<(string AgentId, DateOnly Date)> carryInDays,
        CancellationToken cancellationToken)
    {
        var shiftWorkers = context.Agents.Where(a => a.PerformsShiftWork).ToList();
        var tokensByAgent = shiftWorkers.ToDictionary(
            a => a.Id,
            a => scenario.Tokens.Where(t => string.Equals(t.AgentId, a.Id, StringComparison.Ordinal)).ToList(),
            StringComparer.Ordinal);
        var singlesByAgent = shiftWorkers.ToDictionary(
            a => a.Id,
            a => SwappableSingles(tokensByAgent[a.Id], a.Id, carryInDays),
            StringComparer.Ordinal);
        var rotationByAgent = shiftWorkers.ToDictionary(
            a => a.Id,
            a => AgentRotationAssessor.Assess(tokensByAgent[a.Id], a.Id, context, rotation),
            StringComparer.Ordinal);

        for (var a = 0; a < shiftWorkers.Count; a++)
        {
            var agentA = shiftWorkers[a];
            if (rotationByAgent[agentA.Id].InBlockChanges == 0)
            {
                continue;
            }

            var segmentsA = KindSegments(singlesByAgent[agentA.Id]).ToList();

            for (var b = 0; b < shiftWorkers.Count; b++)
            {
                if (b == a)
                {
                    continue;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                var agentB = shiftWorkers[b];
                foreach (var segmentA in segmentsA)
                {
                    var segmentB = new List<CoreToken>(segmentA.Count);
                    foreach (var tokenA in segmentA)
                    {
                        if (!singlesByAgent[agentB.Id].TryGetValue(tokenA.Date, out var tokenB))
                        {
                            break;
                        }

                        segmentB.Add(tokenB);
                    }

                    if (segmentB.Count != segmentA.Count
                        || segmentB.All(t => t.ShiftTypeIndex == segmentA[0].ShiftTypeIndex))
                    {
                        continue;
                    }

                    var before = rotationByAgent[agentA.Id].Add(rotationByAgent[agentB.Id]);
                    var after = AgentRotationAssessor.Assess(
                            Exchange(tokensByAgent[agentA.Id], segmentA, segmentB), agentA.Id, context, rotation)
                        .Add(AgentRotationAssessor.Assess(
                            Exchange(tokensByAgent[agentB.Id], segmentB, segmentA), agentB.Id, context, rotation));
                    if (!IsPurerWithoutRotationLoss(after, before))
                    {
                        continue;
                    }

                    var candidate = KindBlockSwap.TrySwap(scenario, context, agentA, segmentA, agentB, segmentB);
                    if (candidate is null)
                    {
                        continue;
                    }

                    evaluator.Evaluate(candidate, context);
                    if (evaluator.Compare(candidate, scenario) < 0)
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The agent's days that hold exactly one unlocked token and do not continue an open carry-in package.
    /// </summary>
    private static Dictionary<DateOnly, CoreToken> SwappableSingles(
        List<CoreToken> tokens, string agentId, HashSet<(string AgentId, DateOnly Date)> carryInDays)
    {
        var singles = new Dictionary<DateOnly, CoreToken>();
        foreach (var day in tokens.GroupBy(t => t.Date))
        {
            var only = day.Count() == 1 ? day.First() : null;
            if (only is not null && !only.IsLocked && !carryInDays.Contains((agentId, day.Key)))
            {
                singles[day.Key] = only;
            }
        }

        return singles;
    }

    private static HashSet<(string AgentId, DateOnly Date)> CarryInDays(CoreWizardContext context)
    {
        var anchor = CarryInContinuation.FirstPlannableDay(context);
        var days = new HashSet<(string AgentId, DateOnly Date)>();
        foreach (var package in CarryInContinuation.Detect(context, anchor))
        {
            for (var offset = 0; offset < package.RemainingDays; offset++)
            {
                days.Add((package.AgentId, anchor.AddDays(offset)));
            }
        }

        return days;
    }

    private static IEnumerable<CoreToken> Exchange(
        List<CoreToken> own, List<CoreToken> leaving, List<CoreToken> arriving)
        => own.Where(t => !leaving.Any(l => ReferenceEquals(l, t))).Concat(arriving);

    /// <summary>
    /// Maximal runs of consecutive days on which the agent works one swappable token of the same kind.
    /// </summary>
    private static IEnumerable<List<CoreToken>> KindSegments(Dictionary<DateOnly, CoreToken> singles)
    {
        var run = new List<CoreToken>();
        foreach (var token in singles.Values.OrderBy(t => t.Date))
        {
            if (run.Count > 0
                && (token.Date != run[^1].Date.AddDays(1) || token.ShiftTypeIndex != run[0].ShiftTypeIndex))
            {
                yield return run;
                run = [];
            }

            run.Add(token);
        }

        if (run.Count > 0)
        {
            yield return run;
        }
    }

    private static bool IsPurerWithoutRotationLoss(RotationAssessment candidate, RotationAssessment current)
        => candidate.NonIdealTransitions <= current.NonIdealTransitions
            && candidate.InBlockChanges < current.InBlockChanges;
}
