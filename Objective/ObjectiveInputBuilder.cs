// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Constraints;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Objective;

/// <summary>
/// The two engine adapters that project each engine's plan representation onto the engine-neutral
/// <see cref="ObjectiveInput"/>: Wizard-1's <see cref="CoreScenario"/> token genome and the
/// Harmonizer/W4 <see cref="HarmonyBitmap"/>. Both source the static context (agents, shifts,
/// eligibility, preferences, breaks) from the same <see cref="CoreWizardContext"/>, so the identical
/// gate and objective run against either engine. Break hours always come from the context (never
/// double-sourced from bitmap break cells), keeping the canonical-hours definition identical.
/// </summary>
public static class ObjectiveInputBuilder
{
    public static ObjectiveInput FromScenario(CoreScenario scenario, CoreWizardContext context)
    {
        var assignments = new List<AssignmentView>(scenario.Tokens.Count);
        foreach (var token in scenario.Tokens)
        {
            assignments.Add(AssignmentView.FromToken(token));
        }

        return Build(assignments, context);
    }

    public static ObjectiveInput FromBitmap(HarmonyBitmap bitmap, CoreWizardContext context)
    {
        var assignments = new List<AssignmentView>(bitmap.RowCount * bitmap.DayCount);
        for (var r = 0; r < bitmap.RowCount; r++)
        {
            var agentId = bitmap.Rows[r].Id;
            for (var d = 0; d < bitmap.DayCount; d++)
            {
                var cell = bitmap.GetCell(r, d);
                if (cell.Symbol is CellSymbol.Free or CellSymbol.Break)
                {
                    // Free is no assignment; break hours enter via the context (CanonicalHours), so
                    // break cells are skipped here to avoid double-counting and to keep the gate's
                    // CheckBreakBlocker from flagging a break cell against its own blocker.
                    continue;
                }

                assignments.Add(new AssignmentView(
                    AgentId: agentId,
                    Date: bitmap.Days[d],
                    ShiftRefId: cell.ShiftRefId ?? Guid.Empty,
                    ShiftTypeIndex: ToShiftTypeIndex(cell.Symbol),
                    TotalHours: cell.Hours,
                    StartAt: cell.StartAt,
                    EndAt: cell.EndAt,
                    BlockId: null,
                    IsLocked: cell.IsLocked));
            }
        }

        return Build(assignments, context);
    }

    private static ObjectiveInput Build(List<AssignmentView> assignments, CoreWizardContext context)
    {
        var breakHours = CanonicalHours.BreakHoursByAgent(context);
        var violations = new PlanConstraintChecker().Check(assignments, context);
        return new ObjectiveInput(assignments, breakHours, context, violations);
    }

    /// <summary>
    /// Maps a bitmap <see cref="CellSymbol"/> to the Wizard-1 shift-type index (0=Early, 1=Late,
    /// 2=Night). Other shifts have no dedicated index and map to Early (0) for the shift-type checks;
    /// Other shifts are rare and the only consumers are the PerformsShiftWork / per-day-keyword gates.
    /// </summary>
    private static int ToShiftTypeIndex(CellSymbol symbol) => symbol switch
    {
        CellSymbol.Late => 1,
        CellSymbol.Night => 2,
        _ => 0,
    };
}
