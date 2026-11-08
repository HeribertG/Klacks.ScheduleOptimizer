// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

/// <summary>
/// Placeholder rule of the plan-refining stages (Wizards 2, 3 and 4): a stand-in fills only what real people cannot, so
/// no move may hand a stand-in a work that a real person legally holds. A work that breaks a rule where it stands (rest
/// time, maximum hours, days in a row, a closed day) may go to a stand-in, and a move that hands a stand-in's work to a
/// real person stays allowed. The coverage of these stages never changes (they only rearrange cells), so a stand-in can
/// gain a work only from a real person. The callers (DomainAwareReplaceValidator, RegionAdmission) decide legality.
/// </summary>
public static class StandInWork
{
    /// <summary>
    /// True when the same-day swap of <paramref name="cellA"/> (row of <paramref name="agentA"/>) and
    /// <paramref name="cellB"/> (row of <paramref name="agentB"/>) gives a stand-in a work of a real person in exchange
    /// for a day without work.
    /// </summary>
    public static bool HandsRealWorkToStandIn(BitmapAgent agentA, Cell cellA, BitmapAgent agentB, Cell cellB)
    {
        if (agentA.IsStandIn == agentB.IsStandIn)
        {
            return false;
        }

        var (realCell, standInCell) = agentA.IsStandIn ? (cellB, cellA) : (cellA, cellB);
        return IsWork(realCell.Symbol) && !IsWork(standInCell.Symbol);
    }

    /// <summary>True for a worked cell (Early, Late, Night, Other); false for a free day and an absence.</summary>
    public static bool IsWork(CellSymbol symbol) => symbol is not (CellSymbol.Free or CellSymbol.Break);
}
