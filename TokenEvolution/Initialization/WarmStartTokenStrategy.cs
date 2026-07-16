// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Warm-start population strategy: seeds one scenario from the last accepted plan of the previous
/// period (already mapped onto this period's date axis via <see cref="CoreWizardContext.WarmStartAssignments"/>).
/// Seed tokens are normal, mutable tokens (IsLocked=false) so the GA can still improve on the prior
/// pattern. Invalid cells are dropped (never abort), and each produced individual randomly omits a
/// small share of seed cells so the population keeps diversity around the warm-start pattern.
/// </summary>
public sealed class WarmStartTokenStrategy : ITokenPopulationStrategy
{
    /// <summary>Lower bound of the per-individual share of seed cells that are randomly omitted.</summary>
    private const double MinPerturbationRate = 0.05;

    /// <summary>Upper bound of the per-individual share of seed cells that are randomly omitted.</summary>
    private const double MaxPerturbationRate = 0.15;

    public CoreScenario BuildScenario(CoreWizardContext context, Random rng)
    {
        var lockedTokens = LockedTokenFactory.BuildLockedTokens(
            context.LockedWorks, context.SchedulingMaxConsecutiveDays);
        var tokens = new List<CoreToken>(lockedTokens);

        if (context.WarmStartAssignments.Count == 0)
        {
            return new CoreScenario
            {
                Id = Guid.NewGuid().ToString(),
                Tokens = tokens,
            };
        }

        var agentsById = new Dictionary<string, CoreAgent>(StringComparer.Ordinal);
        foreach (var agent in context.Agents)
        {
            agentsById[agent.Id] = agent;
        }

        var validShiftIds = new HashSet<Guid>();
        foreach (var shift in context.Shifts)
        {
            if (Guid.TryParse(shift.Id, out var shiftId))
            {
                validShiftIds.Add(shiftId);
            }
        }

        var lockedCells = new HashSet<(string AgentId, DateOnly Date)>();
        foreach (var locked in lockedTokens)
        {
            lockedCells.Add((locked.AgentId, locked.Date));
        }

        var perturbationRate = MinPerturbationRate
            + rng.NextDouble() * (MaxPerturbationRate - MinPerturbationRate);

        var survivors = new List<CoreToken>();
        var perAgentAssignments = context.WarmStartAssignments
            .GroupBy(a => a.AgentId)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var group in perAgentAssignments)
        {
            if (!agentsById.TryGetValue(group.Key, out var agent))
            {
                continue;
            }

            var sorted = group.OrderBy(a => a.Date).ThenBy(a => a.StartAt);
            foreach (var assignment in sorted)
            {
                if (!validShiftIds.Contains(assignment.ShiftRefId))
                {
                    continue;
                }

                if (rng.NextDouble() < perturbationRate)
                {
                    continue;
                }

                if (lockedCells.Contains((assignment.AgentId, assignment.Date)))
                {
                    continue;
                }

                var shiftTypeIndex = ShiftTypeInference.FromStartTime(
                    TimeOnly.FromDateTime(assignment.StartAt));

                if (!SlotConstraintFilter.IsValidAssignment(
                        agent,
                        assignment.Date,
                        shiftTypeIndex,
                        assignment.ShiftRefId,
                        assignment.TotalHours,
                        context,
                        tokens,
                        assignment.StartAt,
                        assignment.EndAt))
                {
                    continue;
                }

                var token = new CoreToken(
                    WorkIds: [],
                    ShiftTypeIndex: shiftTypeIndex,
                    Date: assignment.Date,
                    TotalHours: assignment.TotalHours,
                    StartAt: assignment.StartAt,
                    EndAt: assignment.EndAt,
                    BlockId: Guid.NewGuid(),
                    PositionInBlock: 0,
                    IsLocked: false,
                    LocationContext: null,
                    ShiftRefId: assignment.ShiftRefId,
                    AgentId: assignment.AgentId);

                tokens.Add(token);
                survivors.Add(token);
            }
        }

        var finalTokens = new List<CoreToken>(lockedTokens);
        finalTokens.AddRange(ConsecutiveDayBlockAssigner.Assign(
            survivors,
            t => t.AgentId,
            t => t.Date,
            t => t.StartAt,
            context.SchedulingMaxConsecutiveDays,
            (token, blockId, positionInBlock) => token with { BlockId = blockId, PositionInBlock = positionInBlock }));

        return new CoreScenario
        {
            Id = Guid.NewGuid().ToString(),
            Tokens = finalTokens,
        };
    }
}
