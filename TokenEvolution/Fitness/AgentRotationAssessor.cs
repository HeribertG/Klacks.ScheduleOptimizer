// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Common.Rotation;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

/// <summary>
/// Wizard 1's per-agent reading of the rotation rule of <see cref="ShiftRotation"/>: the agent's planned tokens plus the
/// worked shifts before the period, assessed against the kinds the agent may work. Shared by the stage-3 block-order
/// score and by operators that need the rotation cost of a single agent without evaluating the whole plan.
/// </summary>
public static class AgentRotationAssessor
{
    /// <summary>A day inside a block that departs from the block's kind costs half of a non-ideal block change:
    /// rotation between blocks ranks above purity inside a block (SPEC-ROTATION-2026-10-08 rule 4).</summary>
    public const double DefaultInBlockChangePenalty = 0.5;

    /// <summary>A block change that misses the ideal successor; the unit of the rotation cost.</summary>
    public const double NonIdealTransitionPenalty = 1.0;

    /// <summary>
    /// Assesses one agent's tokens together with the agent's carry-in shifts.
    /// </summary>
    /// <param name="agentTokens">Planned tokens of the agent, in any order</param>
    /// <param name="agentId">The agent whose carry-in and allowed kinds apply</param>
    /// <param name="context">Wizard context of the run</param>
    /// <param name="rotation">Cached rotation inputs of the context</param>
    public static RotationAssessment Assess(
        IEnumerable<CoreToken> agentTokens, string agentId, CoreWizardContext context, RotationContext rotation)
    {
        var days = ShiftRotation.DaysOf(agentTokens
            .Select(t => (t.Date, t.ShiftTypeIndex, t.StartAt, t.EndAt))
            .Concat(rotation.BoundaryShiftsOf(agentId)));
        return ShiftRotation.Assess(
            days,
            context.PeriodFrom,
            (kind, blockDays) => rotation.IsAllowedOnAnyDay(agentId, kind, blockDays));
    }

    /// <summary>Weighted rotation violations of an assessment; lower is better.</summary>
    /// <param name="assessment">Assessment of one or more agents</param>
    /// <param name="inBlockChangePenalty">Cost of one in-block kind change in units of a non-ideal block change</param>
    public static double Violations(RotationAssessment assessment, double inBlockChangePenalty)
        => assessment.InBlockChanges * inBlockChangePenalty + assessment.NonIdealTransitions * NonIdealTransitionPenalty;
}
