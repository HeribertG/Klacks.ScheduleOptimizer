// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Builds an initial scenario by randomly assigning a valid agent to each shift slot.
/// Starts from the locked tokens bootstrap and fills empty slots via <see cref="SlotConstraintFilter"/>.
/// Slots without any valid agent remain unassigned; the GA may fill them later.
/// </summary>
public sealed class RandomTokenStrategy : ITokenPopulationStrategy
{
    public CoreScenario BuildScenario(CoreWizardContext context, Random rng)
    {
        var tokens = new List<CoreToken>(
            LockedTokenFactory.BuildLockedTokens(context.LockedWorks, context.SchedulingMaxConsecutiveDays));

        foreach (var slot in context.Shifts)
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

            var candidates = context.Agents
                .Where(agent => SlotConstraintFilter.IsValidAssignment(
                    agent, slotDate.Value, shiftTypeIndex, slotHours, context, tokens))
                .ToList();

            if (candidates.Count == 0)
            {
                continue;
            }

            var chosen = candidates[rng.Next(candidates.Count)];

            tokens.Add(new CoreToken(
                WorkIds: [],
                ShiftTypeIndex: shiftTypeIndex,
                Date: slotDate.Value,
                TotalHours: slotHours,
                StartAt: slotDate.Value.ToDateTime(start),
                EndAt: slotDate.Value.ToDateTime(end),
                BlockId: Guid.NewGuid(),
                PositionInBlock: 0,
                IsLocked: false,
                LocationContext: null,
                ShiftRefId: Guid.TryParse(slot.Id, out var shiftRef) ? shiftRef : Guid.Empty,
                AgentId: chosen.Id));
        }

        return new CoreScenario
        {
            Id = Guid.NewGuid().ToString(),
            Tokens = tokens,
        };
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
