// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Common.Rotation;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Diagnostics;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// Rotation repair on the finished plan: exchanges two whole kind blocks of different kinds that cover exactly the
/// same calendar days between two agents, so both agents keep every worked day and the package structure stays as it
/// was, while each block lands where it continues the agent's rotation (SPEC-ROTATION-2026-10-08). A swap is accepted
/// only when the two agents' rotation improves in the spec's order — fewer non-ideal block changes, or as many and
/// fewer kind changes inside blocks (rule 4: rotation before purity, never a weighted trade) — the plan's block order
/// rises and the lexicographic fitness improves strictly, so hard rules, coverage and hours are never traded away.
/// The two blocks may carry different hours; hours then move between the agents and only the strict comparison
/// (stages 1 and 2) decides whether that is acceptable.
/// No swap may raise the plan's count of mixed-kind packages (rule 7 outranks the rotation of rule 8).
/// With <c>includePrefixSwaps</c> a balancer that finds no whole-block swap also tries handing the first days of a
/// kind block to an agent working another kind on those days.
/// Locked tokens and runs continuing carry-in work are never swapped (<see cref="KindBlockSwap.BuildKindBlocks"/>).
/// Deterministic: candidate pairs are walked in roster order and block start order, first improvement wins.
/// </summary>
/// <param name="includePrefixSwaps">Also try prefix swaps when no whole-block swap qualifies</param>
public sealed class RotationBalancer(bool includePrefixSwaps = false)
{
    /// <summary>Upper bound on accepted swaps per invocation; a safety net, not a tuning knob.</summary>
    private const int MaxSwaps = 48;

    /// <summary>
    /// Returns a strictly better plan produced by whole-block rotation swaps, or the unchanged input.
    /// </summary>
    /// <param name="scenario">Plan to repair; never modified</param>
    /// <param name="context">Wizard context supplying the roster, the rules and the ban list</param>
    /// <param name="evaluator">Fitness evaluator used as the acceptance gate</param>
    /// <param name="diagnostics">Optional sink for the candidate census; null keeps the production path free of counting</param>
    /// <param name="cancellationToken">Stops the repair between candidates and keeps the best plan reached so far</param>
    public CoreScenario Apply(
        CoreScenario scenario,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
        Action<string>? diagnostics = null,
        CancellationToken cancellationToken = default)
    {
        if (context.Agents.Count < 2 || scenario.Tokens.Count == 0)
        {
            return scenario;
        }

        var census = diagnostics is null ? null : new RotationBalanceCensus();
        var rotation = RotationContext.For(context);
        var current = scenario;
        for (var i = 0; i < MaxSwaps && !cancellationToken.IsCancellationRequested; i++)
        {
            var swapped = FindImprovingSwap(current, context, evaluator, rotation, census, includePrefixSwaps, cancellationToken);
            if (swapped is null)
            {
                break;
            }

            current = swapped;
        }

        census?.Report(diagnostics!);
        return current;
    }

    private static CoreScenario? FindImprovingSwap(
        CoreScenario scenario,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
        RotationContext rotation,
        RotationBalanceCensus? census,
        bool includePrefixSwaps,
        CancellationToken cancellationToken)
    {
        var current = new RotationSwapBase(
            scenario,
            evaluator.EvaluateDetailed(scenario, context).Stage3Components.BlockOrder,
            MixedKindPackageTrace.Count(scenario),
            new Dictionary<string, RotationAssessment>(StringComparer.Ordinal));
        var tokensByAgent = scenario.Tokens
            .GroupBy(t => t.AgentId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var blocksByAgent = new Dictionary<string, List<KindBlock>>(StringComparer.Ordinal);
        foreach (var agent in context.Agents)
        {
            blocksByAgent[agent.Id] = KindBlockSwap.BuildKindBlocks(scenario.Tokens, agent.Id, context);
            current.RotationByAgent[agent.Id] = RotationOf(
                tokensByAgent.GetValueOrDefault(agent.Id, []), agent, context, rotation);
        }

        for (var a = 0; a < context.Agents.Count; a++)
        {
            var agentA = context.Agents[a];
            foreach (var blockA in blocksByAgent[agentA.Id])
            {
                for (var b = a + 1; b < context.Agents.Count; b++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return null;
                    }

                    var agentB = context.Agents[b];
                    foreach (var blockB in blocksByAgent[agentB.Id])
                    {
                        var overlap = KindBlockSwap.SharedDays(blockA, blockB);
                        if (overlap is null || !overlap.CoversBothBlocks)
                        {
                            continue;
                        }

                        census?.Count(RotationBalanceCensus.Stage.SameDayPairs);
                        var candidate = KindBlockSwap.TrySwap(
                            scenario, context, agentA, overlap.FromA, agentB, overlap.FromB);
                        if (candidate is not null
                            && Accepts(candidate, current, agentA, agentB, context, evaluator, rotation, census))
                        {
                            return candidate;
                        }
                    }
                }
            }
        }

        return includePrefixSwaps
            ? FindImprovingPrefixSwap(scenario, context, evaluator, rotation, current, blocksByAgent, cancellationToken)
            : null;
    }

    /// <summary>
    /// Fallback when no whole block can be exchanged: hands the first days of a kind block to another agent who works
    /// a different kind on exactly those days. The gate differs from <see cref="Accepts"/> on purpose: the non-ideal
    /// block changes must fall strictly and the block order may drop, because a prefix swap trades purity for rotation
    /// and the spec ranks rotation above purity (rule 4). The mixed-package count may not rise and the lexicographic
    /// fitness must improve strictly.
    /// </summary>
    private static CoreScenario? FindImprovingPrefixSwap(
        CoreScenario scenario,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
        RotationContext rotation,
        RotationSwapBase current,
        Dictionary<string, List<KindBlock>> blocksByAgent,
        CancellationToken cancellationToken)
    {
        var singlesByAgent = blocksByAgent.ToDictionary(
            p => p.Key,
            p => p.Value.SelectMany(b => b.Tokens).ToDictionary(t => t.Date),
            StringComparer.Ordinal);
        foreach (var agentA in context.Agents)
        {
            foreach (var blockA in blocksByAgent[agentA.Id])
            {
                foreach (var agentB in context.Agents.Where(b => !ReferenceEquals(b, agentA)))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return null;
                    }

                    var swapped = FirstImprovingPrefix(
                        scenario, context, evaluator, rotation, current, agentA, blockA, agentB, singlesByAgent[agentB.Id]);
                    if (swapped is not null)
                    {
                        return swapped;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Grows the prefix of <paramref name="blockA"/> day by day while agent B works one swappable token of another kind
    /// on each of those days, and returns the first prefix swap that passes the gate.
    /// </summary>
    private static CoreScenario? FirstImprovingPrefix(
        CoreScenario scenario,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
        RotationContext rotation,
        RotationSwapBase current,
        CoreAgent agentA,
        KindBlock blockA,
        CoreAgent agentB,
        Dictionary<DateOnly, CoreToken> singlesB)
    {
        var before = current.RotationByAgent[agentA.Id].Add(current.RotationByAgent[agentB.Id]);
        var fromA = new List<CoreToken>();
        var fromB = new List<CoreToken>();
        foreach (var tokenA in blockA.Tokens)
        {
            if (!singlesB.TryGetValue(tokenA.Date, out var tokenB) || tokenB.ShiftTypeIndex == blockA.Kind)
            {
                return null;
            }

            fromA.Add(tokenA);
            fromB.Add(tokenB);
            var candidate = KindBlockSwap.TrySwap(scenario, context, agentA, [.. fromA], agentB, [.. fromB]);
            if (candidate is null)
            {
                continue;
            }

            var after = RotationOf(OwnTokens(candidate, agentA.Id), agentA, context, rotation)
                .Add(RotationOf(OwnTokens(candidate, agentB.Id), agentB, context, rotation));
            if (after.NonIdealTransitions >= before.NonIdealTransitions
                || MixedKindPackageTrace.Count(candidate) > current.MixedPackages)
            {
                continue;
            }

            evaluator.Evaluate(candidate, context);
            if (evaluator.Compare(candidate, current.Scenario) < 0)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// The acceptance chain for one filter-legal candidate, cheapest check first: the two agents' rotation in the
    /// spec's order, then the plan's block order, then the strict lexicographic comparison.
    /// </summary>
    private static bool Accepts(
        CoreScenario candidate,
        RotationSwapBase current,
        CoreAgent agentA,
        CoreAgent agentB,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
        RotationContext rotation,
        RotationBalanceCensus? census)
    {
        census?.Count(RotationBalanceCensus.Stage.PassedSlotFilter);
        var before = current.RotationByAgent[agentA.Id].Add(current.RotationByAgent[agentB.Id]);
        var after = RotationOf(OwnTokens(candidate, agentA.Id), agentA, context, rotation)
            .Add(RotationOf(OwnTokens(candidate, agentB.Id), agentB, context, rotation));
        if (!IsBetterRotation(after, before))
        {
            return false;
        }

        census?.Count(RotationBalanceCensus.Stage.BetterRotation);
        if (MixedKindPackageTrace.Count(candidate) > current.MixedPackages)
        {
            return false;
        }

        if (evaluator.EvaluateDetailed(candidate, context).Stage3Components.BlockOrder <= current.BlockOrder)
        {
            return false;
        }

        census?.Count(RotationBalanceCensus.Stage.HigherBlockOrder);
        if (evaluator.Compare(candidate, current.Scenario) >= 0)
        {
            census?.Reject($"{agentA.Id}x{agentB.Id} "
                + $"s1={candidate.FitnessStage1:0.####}/{current.Scenario.FitnessStage1:0.####} "
                + $"s2={candidate.FitnessStage2:0.####}/{current.Scenario.FitnessStage2:0.####} "
                + $"s3={candidate.FitnessStage3:0.####}/{current.Scenario.FitnessStage3:0.####}");
            return false;
        }

        census?.Count(RotationBalanceCensus.Stage.Accepted);
        return true;
    }

    private static IEnumerable<CoreToken> OwnTokens(CoreScenario scenario, string agentId)
        => scenario.Tokens.Where(t => string.Equals(t.AgentId, agentId, StringComparison.Ordinal));

    private static RotationAssessment RotationOf(
        IEnumerable<CoreToken> tokens, CoreAgent agent, CoreWizardContext context, RotationContext rotation)
        => agent.PerformsShiftWork
            ? AgentRotationAssessor.Assess(tokens, agent.Id, context, rotation)
            : default;

    private static bool IsBetterRotation(RotationAssessment candidate, RotationAssessment current)
        => candidate.NonIdealTransitions < current.NonIdealTransitions
            || (candidate.NonIdealTransitions == current.NonIdealTransitions
                && candidate.InBlockChanges < current.InBlockChanges);
}
