// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Projects the engine shapes (assignment view, locked/existing works, bitmap assignments and cells) onto
/// a <see cref="RuleSegment"/>. A default DateTime start or end means "no clock times": such a segment
/// would otherwise read as 00:00-00:00, which the CounterRule duration formula turns into 24 hours that
/// overlap every night window. The Other bitmap symbol maps to the unknown shift type (neither Early nor
/// Late) - unlike ObjectiveInputBuilder, which folds it into Early for its keyword gates.
/// </summary>

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public static class RuleSegmentMapper
{
    public static RuleSegment FromAssignment(in AssignmentView assignment) => Create(
        assignment.AgentId, assignment.Date, assignment.StartAt, assignment.EndAt, assignment.ShiftTypeIndex, assignment.TotalHours);

    public static RuleSegment FromLockedWork(CoreLockedWork work) => Create(
        work.AgentId, work.Date, work.StartAt, work.EndAt, work.ShiftTypeIndex, work.TotalHours);

    public static RuleSegment FromExistingWork(CoreExistingWorkBlocker work) => Create(
        work.AgentId, work.Date, work.StartAt, work.EndAt, RuleTimeConstants.UnknownShiftTypeIndex, 0m);

    public static RuleSegment FromBitmapAssignment(BitmapAssignment assignment) => Create(
        assignment.AgentId, assignment.Date, assignment.StartAt, assignment.EndAt, ToShiftTypeIndex(assignment.Symbol), assignment.Hours);

    public static RuleSegment FromCell(string agentId, DateOnly date, Cell cell) => Create(
        agentId, date, cell.StartAt, cell.EndAt, ToShiftTypeIndex(cell.Symbol), cell.Hours);

    public static bool IsWorked(CellSymbol symbol) => symbol is not (CellSymbol.Free or CellSymbol.Break);

    public static int ToShiftTypeIndex(CellSymbol symbol) => symbol switch
    {
        CellSymbol.Early => RuleTimeConstants.EarlyShiftTypeIndex,
        CellSymbol.Late => RuleTimeConstants.LateShiftTypeIndex,
        CellSymbol.Night => RuleTimeConstants.NightShiftTypeIndex,
        _ => RuleTimeConstants.UnknownShiftTypeIndex,
    };

    private static RuleSegment Create(string agentId, DateOnly date, DateTime startAt, DateTime endAt, int shiftTypeIndex, decimal hours)
    {
        var hasTimes = startAt != default && endAt != default;
        return new RuleSegment(
            AgentId: agentId,
            Date: date,
            Start: hasTimes ? TimeOnly.FromDateTime(startAt) : null,
            End: hasTimes ? TimeOnly.FromDateTime(endAt) : null,
            ShiftTypeIndex: shiftTypeIndex,
            Hours: hours);
    }
}
