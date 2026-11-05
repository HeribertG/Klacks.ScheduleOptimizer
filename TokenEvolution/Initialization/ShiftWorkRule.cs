// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// The one shift-work rule every planner applies: an employee without shift work (PerformsShiftWork = false, from
/// the active contract or, without one, the settings defaults) may only receive early duties; an employee with shift
/// work may receive every duty. The shift type comes from <see cref="ShiftTypeInference"/>. Klacks.ScheduleRecovery
/// cannot reference the optimizer and keeps its own copy of the check (LegalityEvaluator); the unit test
/// RecoveryShiftWorkParityTests pins that both agree.
/// </summary>
public static class ShiftWorkRule
{
    /// <summary>True when a duty of the given shift type requires shift work, i.e. is not an early duty.</summary>
    /// <param name="shiftTypeIndex">Shift type from <see cref="ShiftTypeInference"/> (0 early, 1 late, 2 night; any other value counts as not early).</param>
    public static bool RequiresShiftWork(int shiftTypeIndex) => shiftTypeIndex != ShiftTypeInference.EarlyIndex;

    /// <summary>True when an employee with the given shift-work flag may receive a duty of the given shift type.</summary>
    /// <param name="performsShiftWork">The employee's shift-work flag.</param>
    /// <param name="shiftTypeIndex">Shift type from <see cref="ShiftTypeInference"/>.</param>
    public static bool Permits(bool performsShiftWork, int shiftTypeIndex)
        => performsShiftWork || !RequiresShiftWork(shiftTypeIndex);

    /// <summary>
    /// Bitmap form of <see cref="Permits(bool, int)"/>: a free cell and a break are always permitted, an early cell
    /// always, a late, night or other cell only with shift work (Other is never produced from real times and is not
    /// treated as early).
    /// </summary>
    /// <param name="performsShiftWork">The employee's shift-work flag.</param>
    /// <param name="symbol">The cell the employee would hold.</param>
    public static bool Permits(bool performsShiftWork, CellSymbol symbol)
        => performsShiftWork || symbol is CellSymbol.Free or CellSymbol.Break or CellSymbol.Early;
}
