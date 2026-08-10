// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// Hour-neutral shift-kind rebalancing: swaps two work blocks of different kinds between two agents
/// when both blocks cover exactly the same calendar days. Both agents keep every worked day and every
/// hour, so guaranteed-hours and coverage stages are untouched — the swap can only trade the KIND
/// distribution, which is what the shift-kind fairness rule asks for. Each candidate swap is gated by
/// the full assignment filter on both sides (bans, keywords, rest hours at the block edges, daily
/// caps) and then by one of two acceptance paths: the lexicographic fitness improves strictly, or
/// <see cref="ParetoFairnessGate"/> confirms that the fairness rises while no numbered rule from 1 to 8
/// moves the wrong way. A swap can therefore never buy fairness with a rotation, package-constancy or
/// legality regression, but it may cost order loyalty (rule 10) or an unnumbered helper term, both of
/// which the priority order places below fairness.
/// <para>
/// Deterministic by construction: no random source, candidate pairs are walked in roster order and
/// block start order, first improvement wins, bounded by a fixed swap budget.
/// </para>
/// </summary>
public sealed class ShiftKindBalancer
{
    /// <summary>Upper bound on accepted swaps per invocation; a safety net, not a tuning knob.</summary>
    private const int MaxSwaps = 24;

    /// <summary>
    /// Returns a scenario with strictly better fitness produced by same-day block-kind swaps, or the
    /// unchanged input when no improving swap exists.
    /// </summary>
    /// <param name="scenario">Plan to rebalance; never modified</param>
    /// <param name="context">Wizard context supplying the roster, the rules and the ban list</param>
    /// <param name="evaluator">Fitness evaluator used as the acceptance gate</param>
    public CoreScenario Apply(CoreScenario scenario, CoreWizardContext context, TokenFitnessEvaluator evaluator)
    {
        if (context.Agents.Count < 2 || scenario.Tokens.Count == 0)
        {
            return scenario;
        }

        var current = scenario;
        for (var i = 0; i < MaxSwaps; i++)
        {
            var swapped = FindImprovingSwap(current, context, evaluator);
            if (swapped is null)
            {
                break;
            }

            current = swapped;
        }

        return current;
    }

    private static CoreScenario? FindImprovingSwap(
        CoreScenario scenario, CoreWizardContext context, TokenFitnessEvaluator evaluator)
    {
        var current = ParetoFairnessGate.SnapshotOf(scenario, context, evaluator);
        var currentKindFairness = current.ShiftKindFairness;
        var blocksByAgent = new Dictionary<string, List<KindBlock>>(StringComparer.Ordinal);
        foreach (var agent in context.Agents)
        {
            blocksByAgent[agent.Id] = BuildKindBlocks(scenario.Tokens, agent.Id, context);
        }

        for (var a = 0; a < context.Agents.Count; a++)
        {
            var agentA = context.Agents[a];
            foreach (var blockA in blocksByAgent[agentA.Id])
            {
                for (var b = a + 1; b < context.Agents.Count; b++)
                {
                    var agentB = context.Agents[b];
                    foreach (var blockB in blocksByAgent[agentB.Id])
                    {
                        if (blockB.Kind == blockA.Kind
                            || blockB.FirstDay != blockA.FirstDay
                            || blockB.Tokens.Count != blockA.Tokens.Count)
                        {
                            continue;
                        }

                        var candidate = TrySwap(scenario, context, agentA, blockA, agentB, blockB);
                        if (candidate is null)
                        {
                            continue;
                        }

                        if (evaluator.ComputeShiftKindFairnessScore(candidate, context) <= currentKindFairness)
                        {
                            continue;
                        }

                        var proposed = ParetoFairnessGate.SnapshotOf(candidate, context, evaluator);
                        if (evaluator.Compare(candidate, scenario) < 0
                            || ParetoFairnessGate.Accepts(current, proposed))
                        {
                            return candidate;
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the swap result when every token of both blocks passes the assignment filter for its new
    /// owner, otherwise null. The filter runs against the plan with both blocks removed plus the tokens
    /// already re-owned, so rest-hour checks see the growing swapped state.
    /// </summary>
    private static CoreScenario? TrySwap(
        CoreScenario scenario,
        CoreWizardContext context,
        CoreAgent agentA,
        KindBlock blockA,
        CoreAgent agentB,
        KindBlock blockB)
    {
        var removed = new HashSet<CoreToken>(blockA.Tokens);
        removed.UnionWith(blockB.Tokens);
        var working = scenario.Tokens.Where(t => !removed.Contains(t)).ToList();

        foreach (var (token, receiver) in blockB.Tokens.Select(t => (t, agentA))
                     .Concat(blockA.Tokens.Select(t => (t, agentB)))
                     .OrderBy(p => p.t.Date)
                     .ThenBy(p => p.t.StartAt))
        {
            if (!SlotConstraintFilter.IsValidAssignment(
                    receiver, token.Date, token.ShiftTypeIndex, token.ShiftRefId,
                    token.TotalHours, context, working, token.StartAt, token.EndAt))
            {
                return null;
            }

            working.Add(token with
            {
                AgentId = receiver.Id,
                Surcharges = SurchargeEstimator.Estimate(
                    token.TotalHours, token.ShiftTypeIndex, token.Date, receiver),
            });
        }

        return TokenSwapMutation.CloneScenario(scenario, working);
    }

    /// <summary>
    /// Consecutive-day runs of one agent that are pure in kind, hold exactly one shift per day,
    /// contain no locked token and do not continue fixed work from before the period — a carried-in
    /// package must keep its kind, so a run touching the agent's boundary work is never swapped.
    /// </summary>
    private static List<KindBlock> BuildKindBlocks(
        IReadOnlyList<CoreToken> tokens, string agentId, CoreWizardContext context)
    {
        var boundaryDays = new HashSet<DateOnly>();
        foreach (var locked in context.BoundaryLockedWorks)
        {
            if (string.Equals(locked.AgentId, agentId, StringComparison.Ordinal))
            {
                boundaryDays.Add(locked.Date);
            }
        }

        foreach (var blocker in context.BoundaryExistingWorkBlockers)
        {
            if (string.Equals(blocker.AgentId, agentId, StringComparison.Ordinal))
            {
                boundaryDays.Add(blocker.Date);
            }
        }

        var own = tokens
            .Where(t => string.Equals(t.AgentId, agentId, StringComparison.Ordinal))
            .OrderBy(t => t.Date)
            .ThenBy(t => t.StartAt)
            .ToList();

        var blocks = new List<KindBlock>();
        var run = new List<CoreToken>();

        void CloseRun()
        {
            if (run.Count > 0
                && run.All(t => !t.IsLocked)
                && !boundaryDays.Contains(run[0].Date.AddDays(-1)))
            {
                blocks.Add(new KindBlock(run[0].ShiftTypeIndex, run[0].Date, run.ToList()));
            }

            run.Clear();
        }

        for (var i = 0; i < own.Count; i++)
        {
            var token = own[i];
            var continues = run.Count > 0
                && token.Date == run[^1].Date.AddDays(1)
                && token.ShiftTypeIndex == run[0].ShiftTypeIndex;
            var sameDay = run.Count > 0 && token.Date == run[^1].Date;

            if (sameDay)
            {
                run.Clear();
                while (i + 1 < own.Count && own[i + 1].Date == token.Date)
                {
                    i++;
                }

                continue;
            }

            if (!continues)
            {
                CloseRun();
            }

            run.Add(token);
        }

        CloseRun();
        return blocks;
    }

    private sealed record KindBlock(int Kind, DateOnly FirstDay, List<CoreToken> Tokens);
}
