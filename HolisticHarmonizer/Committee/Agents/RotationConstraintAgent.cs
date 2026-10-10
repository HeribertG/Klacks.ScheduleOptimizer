// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Rules;
using Klacks.ScheduleOptimizer.Harmonizer.Scorer;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Committee.Agents;

/// <summary>
/// Judges a swap by the shared rotation rule (SPEC-ROTATION-2026-10-08, read through <see cref="BitmapRotation"/>): it
/// vetoes when the swap raises the rotation cost of the two rows together (kind changes inside a block, non-ideal block
/// changes weighted twice), approves when it lowers it, and abstains otherwise. Until round 4 this agent vetoed three
/// identical shifts in a row — exactly the pure block the rule asks for. A swap that moves a symbol whose run length a
/// MaxConsecutiveOfKind planning rule governs for the row's agent is left to that rule: the agent then abstains.
/// </summary>
/// <param name="governance">Runs governed by planning rules; null = none</param>
public sealed class RotationConstraintAgent : IConstraintAgent
{
    private readonly PlanningRuleRunGovernance? _governance;

    public RotationConstraintAgent(PlanningRuleRunGovernance? governance = null)
    {
        _governance = governance is { IsEmpty: false } ? governance : null;
    }

    public string Name => "Rotation";

    public ConstraintAgentVerdict Evaluate(HarmonyBitmap before, PlanCellSwap swap)
    {
        var cellA = before.GetCell(swap.RowA, swap.DayA);
        var cellB = before.GetCell(swap.RowB, swap.DayB);
        if (IsGoverned(before, swap.RowA, cellB.Symbol) || IsGoverned(before, swap.RowB, cellA.Symbol))
        {
            return new ConstraintAgentVerdict(Name, ConstraintAgentVote.Abstain, "a planning rule governs the moved shift kind");
        }

        var delta = swap.RowA == swap.RowB
            ? RowDelta(before, swap.RowA, new Dictionary<int, Cell> { [swap.DayA] = cellB, [swap.DayB] = cellA })
            : RowDelta(before, swap.RowA, new Dictionary<int, Cell> { [swap.DayA] = cellB })
                + RowDelta(before, swap.RowB, new Dictionary<int, Cell> { [swap.DayB] = cellA });

        if (delta > 0)
        {
            return new ConstraintAgentVerdict(Name, ConstraintAgentVote.Veto, "worsens the shift rotation (kind change inside a block or a non-ideal block change)");
        }

        if (delta < 0)
        {
            return new ConstraintAgentVerdict(Name, ConstraintAgentVote.Approve, "improves the shift rotation");
        }

        return new ConstraintAgentVerdict(Name, ConstraintAgentVote.Abstain, "swap does not change the shift rotation");
    }

    private static int RowDelta(HarmonyBitmap bitmap, int rowIndex, IReadOnlyDictionary<int, Cell> incoming)
    {
        var beforeCost = BitmapRotation.Cost(bitmap, BitmapRotation.DaysOf(bitmap, rowIndex));
        var afterCost = BitmapRotation.Cost(bitmap, BitmapRotation.DaysOf(bitmap, rowIndex, incoming));
        return afterCost - beforeCost;
    }

    private bool IsGoverned(HarmonyBitmap bitmap, int rowIndex, CellSymbol symbol)
        => _governance is not null && _governance.Governs(bitmap.Rows[rowIndex].Id, symbol);
}
