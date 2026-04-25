// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Slot-first strategy that guarantees maximal shift-coverage in the initial scenario.
/// Iterates every (Shift, Date) slot in deterministic order and assigns the valid agent with
/// the highest <see cref="MotivationFormula"/> score. Ties are broken randomly. Slots without any
/// valid agent remain unassigned — that marks the theoretical coverage ceiling for the context.
/// </summary>
public sealed class CoverageFirstTokenStrategy : ITokenPopulationStrategy
{
    public CoreScenario BuildScenario(CoreWizardContext context, Random rng)
    {
        var tokens = new List<CoreToken>(
            LockedTokenFactory.BuildLockedTokens(context.LockedWorks, context.SchedulingMaxConsecutiveDays));

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

            var bestAgent = SelectBestAgent(context, tokens, slotDate, shiftTypeIndex, slotHours, shiftRefId, rng);
            if (bestAgent is null)
            {
                continue;
            }

            tokens.Add(new CoreToken(
                WorkIds: [],
                ShiftTypeIndex: shiftTypeIndex,
                Date: slotDate,
                TotalHours: slotHours,
                StartAt: slotDate.ToDateTime(start),
                EndAt: slotDate.ToDateTime(end),
                BlockId: Guid.NewGuid(),
                PositionInBlock: 0,
                IsLocked: false,
                LocationContext: null,
                ShiftRefId: shiftRefId,
                AgentId: bestAgent.Id));
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
        DateOnly slotDate,
        int shiftTypeIndex,
        decimal slotHours,
        Guid shiftRefId,
        Random rng)
    {
        CoreAgent? best = null;
        var bestMotivation = double.NegativeInfinity;
        var tieTiebreak = 0;

        foreach (var agent in context.Agents)
        {
            if (!SlotConstraintFilter.IsValidAssignment(agent, slotDate, shiftTypeIndex, slotHours, context, tokens))
            {
                continue;
            }

            var motivation = MotivationFormula.Compute(agent, shiftRefId, slotHours, context.ShiftPreferences);
            if (motivation > bestMotivation)
            {
                best = agent;
                bestMotivation = motivation;
                tieTiebreak = rng.Next();
            }
            else if (Math.Abs(motivation - bestMotivation) < 1e-12)
            {
                var challenger = rng.Next();
                if (challenger > tieTiebreak)
                {
                    best = agent;
                    tieTiebreak = challenger;
                }
            }
        }

        return best;
    }
}
