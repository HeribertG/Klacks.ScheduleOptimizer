// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.Constraints.Keywords;

/// <summary>
/// Bit set of the shift kinds an employee works on one day, the common currency of the keyword rules. Early, late and
/// night follow <see cref="ShiftTypeInference"/>; <see cref="Unclassified"/> marks a harmonizer cell whose kind is
/// unknown (<see cref="CellSymbol.Other"/>). A wizard token always has one of the three classified kinds.
/// </summary>
public static class KeywordShiftKinds
{
    public const int None = 0;
    public const int Early = 1;
    public const int Late = 2;
    public const int Night = 4;
    public const int Unclassified = 8;
    public const int AllClassified = Early | Late | Night;
    public const int AnyWork = AllClassified | Unclassified;

    /// <summary>Kind bit of a shift-type index; every index outside early and late counts as night, as in the combiner.</summary>
    /// <param name="shiftTypeIndex">Index from <see cref="ShiftTypeInference"/>.</param>
    public static int FromShiftTypeIndex(int shiftTypeIndex) => shiftTypeIndex switch
    {
        ShiftTypeInference.EarlyIndex => Early,
        ShiftTypeInference.LateIndex => Late,
        _ => Night,
    };

    /// <summary>Kind bit of a harmonizer cell; free and break cells are no work.</summary>
    /// <param name="symbol">Symbol of the cell.</param>
    public static int FromCellSymbol(CellSymbol symbol) => symbol switch
    {
        CellSymbol.Early => Early,
        CellSymbol.Late => Late,
        CellSymbol.Night => Night,
        CellSymbol.Other => Unclassified,
        _ => None,
    };
}
