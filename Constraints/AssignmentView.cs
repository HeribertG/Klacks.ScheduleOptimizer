// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Constraints;

/// <summary>
/// Engine-neutral projection of a single (agent, date) assignment, holding exactly the fields
/// the plan-level constraint and objective layers need. Both engines adapt onto it: Wizard-1
/// projects a <c>CoreToken</c>, the Harmonizer projects a non-free bitmap <c>Cell</c>. Keeping the
/// objective layer on this view (instead of CoreScenario/HarmonyBitmap) is what lets one composite
/// objective score either engine's output without a circular dependency.
/// </summary>
/// <param name="AgentId">The agent this assignment belongs to</param>
/// <param name="Date">Calendar date of the assignment</param>
/// <param name="ShiftRefId">Reference to the original shift definition (Guid.Empty for free/break)</param>
/// <param name="ShiftTypeIndex">Shift category: 0=Early, 1=Late, 2=Night</param>
/// <param name="TotalHours">Total paid hours of the assignment (excluding surcharges)</param>
/// <param name="StartAt">Start of the assignment span, used by MinPause/overlap checks</param>
/// <param name="EndAt">End of the assignment span, used by MinPause/overlap checks</param>
/// <param name="BlockId">Optional work-block id for block-scoped diagnostics</param>
/// <param name="IsLocked">True if the assignment is immutable (locked work or break)</param>
public readonly record struct AssignmentView(
    string AgentId,
    DateOnly Date,
    Guid ShiftRefId,
    int ShiftTypeIndex,
    decimal TotalHours,
    DateTime StartAt,
    DateTime EndAt,
    Guid? BlockId,
    bool IsLocked)
{
    /// <summary>Projects a Wizard-1 token genome unit onto the engine-neutral view. Single projection
    /// point so the constraint and objective layers can never diverge from the token shape.</summary>
    public static AssignmentView FromToken(CoreToken token) => new(
        AgentId: token.AgentId,
        Date: token.Date,
        ShiftRefId: token.ShiftRefId,
        ShiftTypeIndex: token.ShiftTypeIndex,
        TotalHours: token.TotalHours,
        StartAt: token.StartAt,
        EndAt: token.EndAt,
        BlockId: token.BlockId,
        IsLocked: token.IsLocked);
}

