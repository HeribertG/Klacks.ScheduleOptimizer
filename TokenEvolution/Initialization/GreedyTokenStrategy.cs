// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Goal 1 (highest priority): every shift slot must be covered.
/// Goal 2: every agent should reach its GuaranteedHours target, filled strictly top-down.
/// Phase 1 walks agents in a fixed order (largest initial deficit first) and fills each agent
/// completely up to its target before moving on; later agents take whatever slots remain.
/// Phase 2 enforces 100% coverage: any leftover slot is assigned to the least-loaded agent
/// (constraint-valid first, falling back to force-assign).
/// </summary>
public sealed class GreedyTokenStrategy : ITokenPopulationStrategy
{
    /// <summary>Probability of swapping to a lower-motivation slot for diversity (0..1).</summary>
    public double Epsilon { get; init; } = 0.1;

    public CoreScenario BuildScenario(CoreWizardContext context, Random rng)
    {
        var tokens = new List<CoreToken>(
            LockedTokenFactory.BuildLockedTokens(context.LockedWorks, context.SchedulingMaxConsecutiveDays));

        var remainingSlots = context.Shifts.ToList();
        var hoursAssigned = new Dictionary<string, double>();
        foreach (var agent in context.Agents)
        {
            hoursAssigned[agent.Id] = 0;
        }

        var scheduleIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < context.Agents.Count; i++)
        {
            scheduleIndex[context.Agents[i].Id] = i;
        }

        var agentQueue = context.Agents
            .OrderByDescending(a => RemainingTarget(a))
            .ThenByDescending(a => a.FullTime)
            .ThenBy(a => scheduleIndex[a.Id])
            .ToList();

        foreach (var agent in agentQueue)
        {
            if (remainingSlots.Count == 0)
            {
                break;
            }

            var target = ResolveTarget(agent);

            while (hoursAssigned[agent.Id] + agent.CurrentHours < target)
            {
                var ranked = remainingSlots
                    .Select(slot => (Slot: slot, Score: ScoreSlot(agent, slot, context, tokens)))
                    .Where(item => item.Score.IsValid)
                    .OrderByDescending(item => item.Score.Motivation)
                    .ToList();

                if (ranked.Count == 0)
                {
                    break;
                }

                var pickIndex = ShouldExplore(rng) && ranked.Count > 1 ? rng.Next(1, ranked.Count) : 0;
                var chosen = ranked[pickIndex].Slot;
                remainingSlots.Remove(chosen);

                var slotDate = DateOnly.Parse(chosen.Date);
                var start = ParseTimeOrDefault(chosen.StartTime, new TimeOnly(8, 0));
                var end = ParseTimeOrDefault(chosen.EndTime, start.AddHours(8));
                var shiftTypeIndex = ShiftTypeInference.FromStartTime(start);
                var slotStartUtc = slotDate.ToDateTime(start);
                var slotEndUtc = end <= start ? slotDate.AddDays(1).ToDateTime(end) : slotDate.ToDateTime(end);

                tokens.Add(new CoreToken(
                    WorkIds: [],
                    ShiftTypeIndex: shiftTypeIndex,
                    Date: slotDate,
                    TotalHours: (decimal)chosen.Hours,
                    StartAt: slotStartUtc,
                    EndAt: slotEndUtc,
                    BlockId: Guid.NewGuid(),
                    PositionInBlock: 0,
                    IsLocked: false,
                    LocationContext: null,
                    ShiftRefId: Guid.TryParse(chosen.Id, out var shiftRef) ? shiftRef : Guid.Empty,
                    AgentId: agent.Id));

                hoursAssigned[agent.Id] += chosen.Hours;
            }
        }

        EnforceFullCoverage(context, tokens, remainingSlots, hoursAssigned);

        return new CoreScenario
        {
            Id = Guid.NewGuid().ToString(),
            Tokens = tokens,
        };
    }

    private bool ShouldExplore(Random rng) => rng.NextDouble() < Epsilon;

    private static void EnforceFullCoverage(
        CoreWizardContext context,
        List<CoreToken> tokens,
        List<CoreShift> remainingSlots,
        Dictionary<string, double> hoursAssigned)
    {
        if (remainingSlots.Count == 0 || context.Agents.Count == 0)
        {
            return;
        }

        var pending = remainingSlots.ToList();
        remainingSlots.Clear();

        foreach (var slot in pending)
        {
            var slotDate = ParseDateOrNull(slot.Date);
            if (slotDate is null)
            {
                continue;
            }

            var start = ParseTimeOrDefault(slot.StartTime, new TimeOnly(8, 0));
            var end = ParseTimeOrDefault(slot.EndTime, start.AddHours(8));
            var shiftTypeIndex = ShiftTypeInference.FromStartTime(start);
            var slotHours = (decimal)slot.Hours;
            var slotStartUtc = slotDate.Value.ToDateTime(start);
            var slotEndUtc = end <= start ? slotDate.Value.AddDays(1).ToDateTime(end) : slotDate.Value.ToDateTime(end);

            var agent = PickLeastLoaded(context.Agents, hoursAssigned, slotDate.Value, shiftTypeIndex, slotHours, context, tokens, slotStartUtc, slotEndUtc, requireValid: true)
                        ?? PickLeastLoaded(context.Agents, hoursAssigned, slotDate.Value, shiftTypeIndex, slotHours, context, tokens, slotStartUtc, slotEndUtc, requireValid: false);

            if (agent is null)
            {
                continue;
            }

            tokens.Add(new CoreToken(
                WorkIds: [],
                ShiftTypeIndex: shiftTypeIndex,
                Date: slotDate.Value,
                TotalHours: slotHours,
                StartAt: slotStartUtc,
                EndAt: slotEndUtc,
                BlockId: Guid.NewGuid(),
                PositionInBlock: 0,
                IsLocked: false,
                LocationContext: null,
                ShiftRefId: Guid.TryParse(slot.Id, out var shiftRef) ? shiftRef : Guid.Empty,
                AgentId: agent.Id));

            hoursAssigned[agent.Id] = hoursAssigned.TryGetValue(agent.Id, out var h) ? h + slot.Hours : slot.Hours;
        }
    }

    private static CoreAgent? PickLeastLoaded(
        IReadOnlyList<CoreAgent> agents,
        IReadOnlyDictionary<string, double> hoursAssigned,
        DateOnly slotDate,
        int shiftTypeIndex,
        decimal slotHours,
        CoreWizardContext context,
        IReadOnlyList<CoreToken> tokensSoFar,
        DateTime slotStartUtc,
        DateTime slotEndUtc,
        bool requireValid)
    {
        CoreAgent? best = null;
        double bestLoad = double.PositiveInfinity;

        foreach (var agent in agents)
        {
            if (requireValid &&
                !SlotConstraintFilter.IsValidAssignment(agent, slotDate, shiftTypeIndex, slotHours, context, tokensSoFar, slotStartUtc, slotEndUtc))
            {
                continue;
            }

            var assigned = hoursAssigned.TryGetValue(agent.Id, out var h) ? h : 0;
            var load = agent.CurrentHours + assigned;
            if (load < bestLoad)
            {
                bestLoad = load;
                best = agent;
            }
        }

        return best;
    }

    private static double ResolveTarget(CoreAgent agent)
    {
        if (agent.GuaranteedHours > 0)
        {
            return agent.GuaranteedHours;
        }

        return agent.FullTime > 0 ? agent.FullTime : double.PositiveInfinity;
    }

    private static double RemainingTarget(CoreAgent agent)
    {
        var target = ResolveTarget(agent);
        if (double.IsPositiveInfinity(target))
        {
            return double.MaxValue;
        }

        return Math.Max(0, target - agent.CurrentHours);
    }

    private static (bool IsValid, double Motivation) ScoreSlot(
        CoreAgent agent, CoreShift slot, CoreWizardContext context, IReadOnlyList<CoreToken> tokensSoFar)
    {
        var slotDate = ParseDateOrNull(slot.Date);
        if (slotDate is null)
        {
            return (false, 0);
        }

        var start = ParseTimeOrDefault(slot.StartTime, new TimeOnly(8, 0));
        var end = ParseTimeOrDefault(slot.EndTime, start.AddHours((double)slot.Hours));
        var shiftTypeIndex = ShiftTypeInference.FromStartTime(start);
        var slotHours = (decimal)slot.Hours;
        var slotStartUtc = slotDate.Value.ToDateTime(start);
        var slotEndUtc = end <= start ? slotDate.Value.AddDays(1).ToDateTime(end) : slotDate.Value.ToDateTime(end);

        if (!SlotConstraintFilter.IsValidAssignment(agent, slotDate.Value, shiftTypeIndex, slotHours, context, tokensSoFar, slotStartUtc, slotEndUtc))
        {
            return (false, 0);
        }

        var shiftRef = Guid.TryParse(slot.Id, out var parsed) ? parsed : Guid.Empty;
        var motivation = MotivationFormula.Compute(agent, shiftRef, slotHours, context.ShiftPreferences);
        return (true, motivation);
    }

    private static DateOnly? ParseDateOrNull(string date)
    {
        return DateOnly.TryParse(date, out var parsed) ? parsed : null;
    }

    private static TimeOnly ParseTimeOrDefault(string time, TimeOnly fallback)
    {
        return TimeOnly.TryParse(time, out var parsed) ? parsed : fallback;
    }
}
