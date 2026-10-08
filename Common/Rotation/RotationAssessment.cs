// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Common.Rotation;

/// <summary>
/// Result of <see cref="ShiftRotation.Assess"/> for one agent.
/// </summary>
/// <param name="InBlockSteps">Day-to-day steps inside blocks that touch the counted range</param>
/// <param name="InBlockChanges">Of those, days whose kind departs from the kind the block started with</param>
/// <param name="Transitions">Block changes into a counted block that owe a rotation</param>
/// <param name="NonIdealTransitions">Of those, changes that did not go to the ideal kind</param>
public readonly record struct RotationAssessment(
    int InBlockSteps,
    int InBlockChanges,
    int Transitions,
    int NonIdealTransitions)
{
    public RotationAssessment Add(RotationAssessment other) => new(
        InBlockSteps + other.InBlockSteps,
        InBlockChanges + other.InBlockChanges,
        Transitions + other.Transitions,
        NonIdealTransitions + other.NonIdealTransitions);
}
