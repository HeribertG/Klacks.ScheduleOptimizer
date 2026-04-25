// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Builds a scenario by filling slots agent-by-agent in descending priority (FullTime, then deficit).
/// For each agent, shifts are assigned in descending motivation order until FullTime is reached.
/// An optional epsilon permits occasional diversity deviations.
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

        var agentQueue = context.Agents
            .OrderByDescending(a => a.FullTime)
            .ThenByDescending(a => a.FullTime - a.CurrentHours)
            .ToList();

        foreach (var agent in agentQueue)
        {
            var target = agent.FullTime > 0 ? agent.FullTime : double.PositiveInfinity;

            while (hoursAssigned[agent.Id] + agent.CurrentHours < target)
            {
                var ranked = remainingSlots
                    .Select(slot => (Slot: slot, Motivation: EvaluateSlot(agent, slot, context, tokens)))
                    .Where(item => item.Motivation > 0)
                    .OrderByDescending(item => item.Motivation)
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

                tokens.Add(new CoreToken(
                    WorkIds: [],
                    ShiftTypeIndex: shiftTypeIndex,
                    Date: slotDate,
                    TotalHours: (decimal)chosen.Hours,
                    StartAt: slotDate.ToDateTime(start),
                    EndAt: slotDate.ToDateTime(end),
                    BlockId: Guid.NewGuid(),
                    PositionInBlock: 0,
                    IsLocked: false,
                    LocationContext: null,
                    ShiftRefId: Guid.TryParse(chosen.Id, out var shiftRef) ? shiftRef : Guid.Empty,
                    AgentId: agent.Id));

                hoursAssigned[agent.Id] += chosen.Hours;
            }
        }

        return new CoreScenario
        {
            Id = Guid.NewGuid().ToString(),
            Tokens = tokens,
        };
    }

    private bool ShouldExplore(Random rng) => rng.NextDouble() < Epsilon;

    private static double EvaluateSlot(
        CoreAgent agent, CoreShift slot, CoreWizardContext context, IReadOnlyList<CoreToken> tokensSoFar)
    {
        var slotDate = ParseDateOrNull(slot.Date);
        if (slotDate is null)
        {
            return 0;
        }

        var start = ParseTimeOrDefault(slot.StartTime, new TimeOnly(8, 0));
        var shiftTypeIndex = ShiftTypeInference.FromStartTime(start);
        var slotHours = (decimal)slot.Hours;

        if (!SlotConstraintFilter.IsValidAssignment(agent, slotDate.Value, shiftTypeIndex, slotHours, context, tokensSoFar))
        {
            return 0;
        }

        var shiftRef = Guid.TryParse(slot.Id, out var parsed) ? parsed : Guid.Empty;
        return MotivationFormula.Compute(agent, shiftRef, slotHours, context.ShiftPreferences);
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
