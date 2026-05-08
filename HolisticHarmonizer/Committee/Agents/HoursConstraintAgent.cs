// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using System.Globalization;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Committee.Agents;

/// <summary>
/// Votes on whether a swap moves both involved rows toward (approve) or away from (veto)
/// their target hours. Uses each cell's <c>Hours</c> field directly so it is consistent with
/// the harmony scorer. Abstains when the swap exchanges cells with identical hour values
/// (no net change) or when only one row improves while the other worsens.
/// </summary>
public sealed class HoursConstraintAgent : IConstraintAgent
{
    public string Name => "Hours";

    public ConstraintAgentVerdict Evaluate(HarmonyBitmap before, PlanCellSwap swap)
    {
        var cellA = before.GetCell(swap.RowA, swap.DayA);
        var cellB = before.GetCell(swap.RowB, swap.DayB);
        if (cellA.Hours == cellB.Hours)
        {
            return Abstain("identical hour values, no net change");
        }

        var rowAHours = SumRowHours(before, swap.RowA);
        var rowBHours = SumRowHours(before, swap.RowB);
        var rowATarget = before.Rows[swap.RowA].TargetHours;
        var rowBTarget = before.Rows[swap.RowB].TargetHours;

        var rowANewHours = rowAHours - cellA.Hours + cellB.Hours;
        var rowBNewHours = rowBHours - cellB.Hours + cellA.Hours;

        var rowABefore = Math.Abs(rowAHours - rowATarget);
        var rowAAfter = Math.Abs(rowANewHours - rowATarget);
        var rowBBefore = Math.Abs(rowBHours - rowBTarget);
        var rowBAfter = Math.Abs(rowBNewHours - rowBTarget);

        var rowAImproves = rowAAfter < rowABefore;
        var rowBImproves = rowBAfter < rowBBefore;
        var rowAWorsens = rowAAfter > rowABefore;
        var rowBWorsens = rowBAfter > rowBBefore;

        if (rowAWorsens && rowBWorsens)
        {
            var reason = string.Format(
                CultureInfo.InvariantCulture,
                "swap pushes both rows away from target ({0} {1:F1}h→{2:F1}h, {3} {4:F1}h→{5:F1}h)",
                before.Rows[swap.RowA].DisplayName, rowAHours, rowANewHours,
                before.Rows[swap.RowB].DisplayName, rowBHours, rowBNewHours);
            return new ConstraintAgentVerdict(Name, ConstraintAgentVote.Veto, reason);
        }

        if ((rowAImproves || rowABefore == rowAAfter) && (rowBImproves || rowBBefore == rowBAfter)
            && (rowAImproves || rowBImproves))
        {
            return new ConstraintAgentVerdict(Name, ConstraintAgentVote.Approve, "moves at least one row toward target without harming the other");
        }

        return Abstain("mixed effect on hour deviations");
    }

    private static decimal SumRowHours(HarmonyBitmap bitmap, int rowIndex)
    {
        var sum = 0m;
        for (var d = 0; d < bitmap.DayCount; d++)
        {
            sum += bitmap.GetCell(rowIndex, d).Hours;
        }
        return sum;
    }

    private ConstraintAgentVerdict Abstain(string reason)
        => new(Name, ConstraintAgentVote.Abstain, reason);
}
