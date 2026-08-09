// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Constraints;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// Structure-neutral order rebalancing (rule 10): exchanges two assignments of the SAME day, the same
/// shift kind and the same clock span between two employees, so only the order changes. Both keep the
/// worked day, the hours, the kind and the rest windows, and every slot keeps exactly one carrier —
/// the swap can therefore only trade order loyalty, never coverage, guaranteed hours or rotation.
/// <para>
/// Shift preferences outrank order loyalty (owner decision B2), which the gate enforces on both sides:
/// an assignment the current holder PREFERS is never traded away, and no receiver may be handed a
/// shift on its blacklist. The blacklist check is not redundant — <see cref="SlotConstraintFilter"/>
/// does not look at preferences, so this operator would otherwise be the first code path able to
/// create a blacklist violation while claiming neutrality. Assignments belonging to a carried-in
/// package that is still open are excluded as well: continuing that package outranks order loyalty.
/// </para>
/// <para>
/// Deterministic by construction: no random source, days ascending, kinds ascending, employees in
/// roster order, first improvement wins, bounded by a fixed swap budget.
/// </para>
/// </summary>
public sealed class ObjectContinuityBalancer
{
    /// <summary>Upper bound on accepted swaps per invocation; a safety net, not a tuning knob.</summary>
    private const int MaxSwaps = 32;

    /// <summary>
    /// Returns a scenario with strictly better fitness produced by same-day order swaps, or the
    /// unchanged input when no improving swap exists.
    /// </summary>
    /// <param name="scenario">Plan to rebalance; never modified</param>
    /// <param name="context">Wizard context supplying the roster, the rules and the preferences</param>
    /// <param name="evaluator">Fitness evaluator used as the acceptance gate</param>
    public CoreScenario Apply(CoreScenario scenario, CoreWizardContext context, TokenFitnessEvaluator evaluator)
    {
        if (context.Agents.Count < 2 || scenario.Tokens.Count == 0)
        {
            return scenario;
        }

        var preferences = PreferenceIndex.Build(context);
        var protectedDays = BuildCarryInDays(context);
        var rosterPosition = BuildRosterPositions(context);

        var current = scenario;
        for (var i = 0; i < MaxSwaps; i++)
        {
            var swapped = FindImprovingSwap(
                current, context, evaluator, preferences, protectedDays, rosterPosition);
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
        PreferenceIndex preferences,
        IReadOnlySet<(string AgentId, DateOnly Date, Guid ShiftRefId)> protectedDays,
        IReadOnlyDictionary<string, int> rosterPosition)
    {
        var sequences = AgentSequence.BuildAll(scenario.Tokens, rosterPosition);
        var totals = AgentSequence.CountPairs(sequences);

        foreach (var group in GroupInterchangeable(scenario.Tokens, rosterPosition))
        {
            for (var a = 0; a < group.Count - 1; a++)
            {
                for (var b = a + 1; b < group.Count; b++)
                {
                    var candidate = TrySwap(
                        scenario, context, evaluator, preferences, protectedDays,
                        sequences, totals, group[a], group[b]);
                    if (candidate is not null)
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    private static CoreScenario? TrySwap(
        CoreScenario scenario,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
        PreferenceIndex preferences,
        IReadOnlySet<(string AgentId, DateOnly Date, Guid ShiftRefId)> protectedDays,
        IReadOnlyDictionary<string, AgentSequence> sequences,
        PairTotals totals,
        CoreToken first,
        CoreToken second)
    {
        if (!IsTradable(first, protectedDays) || !IsTradable(second, protectedDays))
        {
            return null;
        }

        if (string.Equals(first.AgentId, second.AgentId, StringComparison.Ordinal)
            || string.Equals(first.LocationContext, second.LocationContext, StringComparison.Ordinal))
        {
            return null;
        }

        if (preferences.IsPreferred(first.AgentId, first.ShiftRefId)
            || preferences.IsPreferred(second.AgentId, second.ShiftRefId))
        {
            return null;
        }

        if (preferences.IsBlacklisted(first.AgentId, second.ShiftRefId)
            || preferences.IsBlacklisted(second.AgentId, first.ShiftRefId))
        {
            return null;
        }

        var agentsById = EvaluationContext.For(context).AgentsById;
        if (!agentsById.TryGetValue(first.AgentId, out var firstAgent)
            || !agentsById.TryGetValue(second.AgentId, out var secondAgent))
        {
            return null;
        }

        if (!sequences.TryGetValue(first.AgentId, out var firstSequence)
            || !sequences.TryGetValue(second.AgentId, out var secondSequence)
            || !firstSequence.TryPositionOf(first, out var firstPosition)
            || !secondSequence.TryPositionOf(second, out var secondPosition))
        {
            return null;
        }

        var delta = firstSequence.DeltaAt(firstPosition, second.LocationContext)
            + secondSequence.DeltaAt(secondPosition, first.LocationContext);
        if (!ImprovesLoyalty(totals, delta))
        {
            return null;
        }

        var candidate = BuildSwapped(scenario, context, firstAgent, first, secondAgent, second);
        if (candidate is null)
        {
            return null;
        }

        evaluator.Evaluate(candidate, context);
        return evaluator.Compare(candidate, scenario) < 0 ? candidate : null;
    }

    /// <summary>
    /// Builds the swapped plan when both receivers pass the full assignment filter, otherwise null.
    /// The filter runs against the plan with both assignments removed plus the already re-owned one,
    /// so the second check sees the state the first one created.
    /// </summary>
    private static CoreScenario? BuildSwapped(
        CoreScenario scenario,
        CoreWizardContext context,
        CoreAgent firstAgent,
        CoreToken first,
        CoreAgent secondAgent,
        CoreToken second)
    {
        var working = scenario.Tokens
            .Where(t => !ReferenceEquals(t, first) && !ReferenceEquals(t, second)).ToList();

        foreach (var (token, receiver) in new[] { (second, firstAgent), (first, secondAgent) })
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

    private static bool ImprovesLoyalty(PairTotals totals, SwitchDelta delta)
    {
        if (delta.Within == 0 && delta.Across == 0)
        {
            return false;
        }

        var before = LocationContinuity.Combine(
            totals.WithinPairs, totals.WithinSwitches, totals.AcrossPairs, totals.AcrossSwitches);
        var after = LocationContinuity.Combine(
            totals.WithinPairs,
            totals.WithinSwitches + delta.Within,
            totals.AcrossPairs,
            totals.AcrossSwitches + delta.Across);

        return after > before;
    }

    private static bool IsTradable(
        CoreToken token, IReadOnlySet<(string AgentId, DateOnly Date, Guid ShiftRefId)> protectedDays)
        => !token.IsLocked
            && token.LocationContext is { Length: > 0 }
            && !protectedDays.Contains((token.AgentId, token.Date, token.ShiftRefId));

    /// <summary>
    /// Assignments grouped by everything a swap must leave untouched: the day, the kind and the exact
    /// clock span including the hours. Grouping on the full span — not on the kind alone — is what
    /// makes the exchange structurally neutral and keeps every employee's assignment order intact, so
    /// the incremental switch delta stays exact. One pass over the plan, groups walked in day, kind
    /// and start order, members in roster order.
    /// </summary>
    private static IEnumerable<List<CoreToken>> GroupInterchangeable(
        IReadOnlyList<CoreToken> tokens, IReadOnlyDictionary<string, int> rosterPosition)
    {
        var groups =
            new Dictionary<(DateOnly, int, DateTime, DateTime, decimal), List<CoreToken>>();

        foreach (var token in tokens)
        {
            var key = (token.Date, token.ShiftTypeIndex, token.StartAt, token.EndAt, token.TotalHours);
            if (!groups.TryGetValue(key, out var members))
            {
                members = [];
                groups[key] = members;
            }

            members.Add(token);
        }

        return groups
            .Where(entry => entry.Value.Count > 1)
            .OrderBy(entry => entry.Key.Item1)
            .ThenBy(entry => entry.Key.Item2)
            .ThenBy(entry => entry.Key.Item3)
            .Select(entry => entry.Value
                .OrderBy(t => rosterPosition.GetValueOrDefault(t.AgentId, int.MaxValue))
                .ThenBy(t => t.AgentId, StringComparer.Ordinal)
                .ThenBy(t => t.ShiftRefId)
                .ToList());
    }

    private static Dictionary<string, int> BuildRosterPositions(CoreWizardContext context)
    {
        var positions = new Dictionary<string, int>(context.Agents.Count, StringComparer.Ordinal);
        for (var i = 0; i < context.Agents.Count; i++)
        {
            positions.TryAdd(context.Agents[i].Id, i);
        }

        return positions;
    }

    /// <summary>
    /// The (employee, day, order) triples owed to a carried-in package that is still open. They are the
    /// construction the carry-in seeder produced and the carry-in fitness term protects; order loyalty
    /// sits below both and must not undo them.
    /// </summary>
    private static HashSet<(string AgentId, DateOnly Date, Guid ShiftRefId)> BuildCarryInDays(
        CoreWizardContext context)
    {
        var owed = new HashSet<(string, DateOnly, Guid)>();
        var anchor = CarryInContinuation.FirstPlannableDay(context);
        foreach (var package in CarryInContinuation.Detect(context, anchor))
        {
            for (var offset = 0; offset < package.RemainingDays; offset++)
            {
                owed.Add((package.AgentId, anchor.AddDays(offset), package.ShiftRefId));
            }
        }

        return owed;
    }
}
