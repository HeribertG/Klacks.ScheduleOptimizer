// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Slot-first strategy that guarantees maximal shift-coverage in the initial scenario.
/// Iterates every (Shift, Date) slot in deterministic order and assigns following the top-down
/// roster rule: among valid agents still below their guaranteed hours the highest
/// <see cref="MotivationFormula"/> score wins (roster position breaks ties, top first); once
/// every valid agent has reached its target the surplus slot goes to the bottom of the roster
/// so the top stays accurate. Slots without any valid agent remain unassigned — that marks the
/// theoretical coverage ceiling for the context.
/// </summary>
public sealed class CoverageFirstTokenStrategy : ITokenPopulationStrategy
{
    public CoreScenario BuildScenario(CoreWizardContext context, Random rng)
    {
        var tokens = new List<CoreToken>(
            LockedTokenFactory.BuildLockedTokens(context.LockedWorks, context.SchedulingMaxConsecutiveDays));

        var hoursByAgent = new Dictionary<string, double>(StringComparer.Ordinal);
        var blockState = new Dictionary<string, AgentBlockState>(StringComparer.Ordinal);
        foreach (var locked in tokens.OrderBy(t => t.Date))
        {
            AddHours(hoursByAgent, locked.AgentId, (double)(locked.TotalHours + locked.Surcharges));
            TrackBlock(blockState, locked.AgentId, locked.Date, locked.ShiftTypeIndex);
        }

        var orderedSlots = context.Shifts
            .OrderBy(s => s.Date, StringComparer.Ordinal)
            .ThenBy(s => s.StartTime, StringComparer.Ordinal)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();

        foreach (var slot in orderedSlots)
        {
            if (!DateOnly.TryParse(slot.Date, out var slotDate))
            {
                continue;
            }

            var start = TimeOnly.TryParse(slot.StartTime, out var parsedStart)
                ? parsedStart
                : new TimeOnly(8, 0);
            var end = TimeOnly.TryParse(slot.EndTime, out var parsedEnd)
                ? parsedEnd
                : start.AddHours((double)slot.Hours);
            var shiftTypeIndex = ShiftTypeInference.FromStartTime(start);
            var slotHours = (decimal)slot.Hours;
            var shiftRefId = Guid.TryParse(slot.Id, out var parsedShift) ? parsedShift : Guid.Empty;
            var slotStartUtc = slotDate.ToDateTime(start);
            var slotEndUtc = end <= start ? slotDate.AddDays(1).ToDateTime(end) : slotDate.ToDateTime(end);

            var bestAgent = SelectBestAgent(context, tokens, hoursByAgent, blockState, slotDate, shiftTypeIndex, slotHours, shiftRefId, slotStartUtc, slotEndUtc);
            if (bestAgent is null)
            {
                continue;
            }

            tokens.Add(new CoreToken(
                WorkIds: [],
                ShiftTypeIndex: shiftTypeIndex,
                Date: slotDate,
                TotalHours: slotHours,
                StartAt: slotStartUtc,
                EndAt: slotEndUtc,
                BlockId: Guid.NewGuid(),
                PositionInBlock: 0,
                IsLocked: false,
                LocationContext: null,
                ShiftRefId: shiftRefId,
                AgentId: bestAgent.Id));
            AddHours(hoursByAgent, bestAgent.Id, (double)slotHours);
            TrackBlock(blockState, bestAgent.Id, slotDate, shiftTypeIndex);
        }

        return new CoreScenario
        {
            Id = Guid.NewGuid().ToString(),
            Tokens = tokens,
        };
    }

    private static CoreAgent? SelectBestAgent(
        CoreWizardContext context,
        IReadOnlyList<CoreToken> tokens,
        IReadOnlyDictionary<string, double> hoursByAgent,
        IReadOnlyDictionary<string, AgentBlockState> blockState,
        DateOnly slotDate,
        int shiftTypeIndex,
        decimal slotHours,
        Guid shiftRefId,
        DateTime slotStartUtc,
        DateTime slotEndUtc)
    {
        // Two-tier selection: candidates that honour the shift-type rotation (a new block must
        // not repeat the previous block's type) are preferred; rotation-violating candidates
        // remain the fallback so coverage never drops below the type-blind behaviour.
        CoreAgent? bestRotated = null;
        var bestRotatedRemaining = double.NegativeInfinity;
        var bestRotatedMotivation = double.NegativeInfinity;
        CoreAgent? bestViolating = null;
        var bestViolatingRemaining = double.NegativeInfinity;
        var bestViolatingMotivation = double.NegativeInfinity;
        CoreAgent? bottomMostSurplusRotated = null;
        CoreAgent? bottomMostSurplus = null;

        foreach (var agent in context.Agents)
        {
            if (!SlotConstraintFilter.IsValidAssignment(agent, slotDate, shiftTypeIndex, slotHours, context, tokens, slotStartUtc, slotEndUtc))
            {
                continue;
            }

            var violatesRotation = ViolatesRotation(agent, blockState, slotDate, shiftTypeIndex);
            var assigned = agent.CurrentHours + hoursByAgent.GetValueOrDefault(agent.Id, 0);
            if (agent.GuaranteedHours > 0 && assigned < agent.GuaranteedHours)
            {
                // Largest remaining target first — self-balancing, so packing never starves the
                // tail into constraint walls. Motivation breaks remaining ties; iteration follows
                // roster order, so strict comparisons keep the top-most agent on full ties.
                var remaining = agent.GuaranteedHours - assigned;
                var motivation = MotivationFormula.Compute(agent, shiftRefId, slotHours, context.ShiftPreferences);
                if (violatesRotation)
                {
                    if (remaining > bestViolatingRemaining
                        || (Math.Abs(remaining - bestViolatingRemaining) < 1e-9 && motivation > bestViolatingMotivation))
                    {
                        bestViolating = agent;
                        bestViolatingRemaining = remaining;
                        bestViolatingMotivation = motivation;
                    }
                }
                else if (remaining > bestRotatedRemaining
                    || (Math.Abs(remaining - bestRotatedRemaining) < 1e-9 && motivation > bestRotatedMotivation))
                {
                    bestRotated = agent;
                    bestRotatedRemaining = remaining;
                    bestRotatedMotivation = motivation;
                }
            }
            else
            {
                // Surplus candidates: the bottom-most roster position eats what is left.
                if (!violatesRotation)
                {
                    bottomMostSurplusRotated = agent;
                }

                bottomMostSurplus = agent;
            }
        }

        return bestRotated ?? bestViolating ?? bottomMostSurplusRotated ?? bottomMostSurplus;
    }

    /// <summary>
    /// True when assigning this slot would start a NEW block with the same shift type as the
    /// agent's previous block — the rotation rule (early → late → night) demands a change.
    /// Day-only workers (PerformsShiftWork = false) are exempt.
    /// </summary>
    private static bool ViolatesRotation(
        CoreAgent agent,
        IReadOnlyDictionary<string, AgentBlockState> blockState,
        DateOnly slotDate,
        int shiftTypeIndex)
    {
        if (!agent.PerformsShiftWork || !blockState.TryGetValue(agent.Id, out var state))
        {
            return false;
        }

        var startsNewBlock = slotDate.DayNumber - state.LastWorkedDate.DayNumber > 1;
        return startsNewBlock && state.CurrentBlockStartShiftType == shiftTypeIndex;
    }

    private static void TrackBlock(
        Dictionary<string, AgentBlockState> blockState, string agentId, DateOnly date, int shiftTypeIndex)
    {
        if (!blockState.TryGetValue(agentId, out var state)
            || date.DayNumber - state.LastWorkedDate.DayNumber > 1)
        {
            blockState[agentId] = new AgentBlockState(date, shiftTypeIndex);
            return;
        }

        blockState[agentId] = state with { LastWorkedDate = date };
    }

    private static void AddHours(Dictionary<string, double> hoursByAgent, string agentId, double hours)
    {
        hoursByAgent[agentId] = hoursByAgent.TryGetValue(agentId, out var existing)
            ? existing + hours
            : hours;
    }

    private sealed record AgentBlockState(DateOnly LastWorkedDate, int CurrentBlockStartShiftType);
}
