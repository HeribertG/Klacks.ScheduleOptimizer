// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Adapters from the engine inputs to a <see cref="RuleEvaluationContext"/>. Wizard 1 contributes its
/// boundary locked and existing works, the bitmap engines their boundary assignments; boundary breaks are
/// left out because a break counts as free. Both engine boundaries only reach the ContextDays window, so a
/// month or year PeriodCount needs the carry-in segments of the rest of the counted period, passed in by
/// the caller (the API loader) as <paramref name="carryIn"/>. The night minimum overlap of the sequence and fairness
/// rules is a required argument: the API reads it from NIGHT_RULE_MIN_OVERLAP_MINUTES (PlanningRuleSet agents carry
/// it), and an engine must pass the same value so engine and validator classify the same segments as night.
/// </summary>

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public static class RuleEvaluationContextFactory
{
    public static RuleEvaluationContext FromWizardContext(
        CoreWizardContext context, int nightRuleMinOverlapMinutes, IReadOnlyList<RuleSegment>? carryIn = null)
    {
        var agents = new List<RuleAgent>(context.Agents.Count);
        foreach (var agent in context.Agents)
        {
            agents.Add(new RuleAgent(agent.Id, agent.NightWindow, WorkloadPercentOf(agent), nightRuleMinOverlapMinutes));
        }

        var boundary = new List<RuleSegment>(
            context.BoundaryLockedWorks.Count + context.BoundaryExistingWorkBlockers.Count + (carryIn?.Count ?? 0));
        foreach (var work in context.BoundaryLockedWorks)
        {
            boundary.Add(RuleSegmentMapper.FromLockedWork(work));
        }

        foreach (var work in context.BoundaryExistingWorkBlockers)
        {
            boundary.Add(RuleSegmentMapper.FromExistingWork(work));
        }

        AppendCarryIn(boundary, carryIn);
        return new RuleEvaluationContext(context.PeriodFrom, context.PeriodUntil, agents, boundary);
    }

    public static RuleEvaluationContext FromBitmap(
        BitmapInput input, int nightRuleMinOverlapMinutes, IReadOnlyList<RuleSegment>? carryIn = null)
    {
        var agents = new List<RuleAgent>(input.Agents.Count);
        foreach (var agent in input.Agents)
        {
            agents.Add(new RuleAgent(
                agent.Id, agent.NightWindow, agent.WorkloadPercent ?? RuleTimeConstants.FullWorkloadPercent, nightRuleMinOverlapMinutes));
        }

        var boundaryAssignments = input.BoundaryAssignments ?? [];
        var boundary = new List<RuleSegment>(boundaryAssignments.Count + (carryIn?.Count ?? 0));
        foreach (var assignment in boundaryAssignments)
        {
            if (RuleSegmentMapper.IsWorked(assignment.Symbol))
            {
                boundary.Add(RuleSegmentMapper.FromBitmapAssignment(assignment));
            }
        }

        AppendCarryIn(boundary, carryIn);
        return new RuleEvaluationContext(input.StartDate, input.EndDate, agents, boundary);
    }

    /// <summary>
    /// Workload share of a wizard agent: guaranteed hours over full-time hours (both per period, from the same
    /// contract). Without full-time hours the agent counts as full time.
    /// </summary>
    public static decimal WorkloadPercentOf(CoreAgent agent)
    {
        if (agent.FullTime <= 0)
        {
            return RuleTimeConstants.FullWorkloadPercent;
        }

        return (decimal)(agent.GuaranteedHours / agent.FullTime) * RuleTimeConstants.FullWorkloadPercent;
    }

    private static void AppendCarryIn(List<RuleSegment> boundary, IReadOnlyList<RuleSegment>? carryIn)
    {
        if (carryIn is not null)
        {
            boundary.AddRange(carryIn);
        }
    }
}
