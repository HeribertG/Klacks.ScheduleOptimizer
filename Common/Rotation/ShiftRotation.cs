// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Common.Rotation;

/// <summary>
/// The one rotation rule of all autofill engines (owner decision 2026-10-08, tests/autofill/SPEC-ROTATION-2026-10-08.md;
/// replaces decision 12b and Wizard 2's "ascending only"):
/// <list type="bullet">
/// <item>A block is a run of worked days whose rest to the previous day is below <see cref="BlockBoundaryRestHours"/>
/// (end of the latest shift to start of the next one, in hours, not calendar days).</item>
/// <item>Inside a block the kind should stay the one the block started with.</item>
/// <item>Between blocks the ideal successor of the previous block's last kind is the next one in early, late, night,
/// early; night to early is the normal cycle step. A kind not allowed on the new block's days is skipped; with at most
/// one allowed kind no rotation is owed. After at least <see cref="LongPauseFreeDays"/> free calendar days the cycle
/// restarts at the first allowed kind from early.</item>
/// <item>Everything is soft: the ideal yields to necessity. Callers weigh a non-ideal block change above a change
/// inside a block (rotation before purity).</item>
/// </list>
/// Carry-in days before the counted range take part as predecessors only.
/// </summary>
public static class ShiftRotation
{
    public const double BlockBoundaryRestHours = 48;
    public const int LongPauseFreeDays = 7;
    public const int KindCount = 3;
    public const int EarlyKindIndex = 0;

    /// <summary>Collapses shifts into date-ordered worked days (earliest and latest kind, first start, last end).</summary>
    /// <param name="shifts">Shifts of one agent in any order</param>
    public static List<RotationDay> DaysOf(IEnumerable<(DateOnly Date, int KindIndex, DateTime StartAt, DateTime EndAt)> shifts)
    {
        return shifts
            .GroupBy(s => s.Date)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var first = g.MinBy(s => s.StartAt);
                var last = g.MaxBy(s => s.EndAt);
                return new RotationDay(g.Key, first.KindIndex, last.KindIndex, first.StartAt, last.EndAt);
            })
            .ToList();
    }

    /// <summary>Splits date-ordered days into blocks.</summary>
    /// <param name="days">Worked days of one agent, ordered by date</param>
    public static List<List<RotationDay>> BuildBlocks(IReadOnlyList<RotationDay> days)
    {
        var blocks = new List<List<RotationDay>>();
        foreach (var day in days)
        {
            if (blocks.Count == 0 || IsBlockBoundary(blocks[^1][^1], day))
            {
                blocks.Add([]);
            }

            blocks[^1].Add(day);
        }

        return blocks;
    }

    public static bool IsBlockBoundary(RotationDay previous, RotationDay next)
        => (next.FirstStart - previous.LastEnd).TotalHours >= BlockBoundaryRestHours;

    /// <summary>Free calendar days between the end of one block and the start of the next.</summary>
    public static bool IsLongPause(RotationDay lastOfPrevious, RotationDay firstOfNext)
        => firstOfNext.Date.DayNumber - DateOnly.FromDateTime(lastOfPrevious.LastEnd).DayNumber - 1 >= LongPauseFreeDays;

    /// <summary>
    /// The kind the next block should have, or null when at most one kind is allowed (no rotation owed).
    /// </summary>
    /// <param name="previousKindIndex">Last kind of the previous block</param>
    /// <param name="longPause">True after a long pause: restart at the first allowed kind from early</param>
    /// <param name="isAllowed">Whether a kind is allowed on the new block's days</param>
    public static int? IdealSuccessor(int previousKindIndex, bool longPause, Func<int, bool> isAllowed)
    {
        var allowedCount = 0;
        for (var kind = 0; kind < KindCount; kind++)
        {
            if (isAllowed(kind))
            {
                allowedCount++;
            }
        }

        if (allowedCount <= 1)
        {
            return null;
        }

        var start = longPause ? EarlyKindIndex : previousKindIndex + 1;
        for (var step = 0; step < KindCount; step++)
        {
            var candidate = ((start + step) % KindCount + KindCount) % KindCount;
            if (isAllowed(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Assesses one agent's days. Blocks entirely before <paramref name="countFrom"/> (carry-in) are only predecessors;
    /// a block change is counted when the new block starts on or after it.
    /// </summary>
    /// <param name="days">Worked days of one agent, carry-in included, ordered by date</param>
    /// <param name="countFrom">First day of the planned range</param>
    /// <param name="isAllowedOnDays">Whether a kind may be worked on at least one of the given days</param>
    public static RotationAssessment Assess(
        IReadOnlyList<RotationDay> days,
        DateOnly countFrom,
        Func<int, IReadOnlyList<DateOnly>, bool> isAllowedOnDays)
    {
        var blocks = BuildBlocks(days);
        var steps = 0;
        var changes = 0;
        var transitions = 0;
        var nonIdeal = 0;

        for (var b = 0; b < blocks.Count; b++)
        {
            var block = blocks[b];
            if (block[^1].Date < countFrom)
            {
                continue;
            }

            for (var i = 1; i < block.Count; i++)
            {
                if (block[i].Date < countFrom)
                {
                    continue;
                }

                steps++;
                if (block[i].FirstKindIndex != block[0].FirstKindIndex)
                {
                    changes++;
                }
            }

            if (b == 0 || block[0].Date < countFrom)
            {
                continue;
            }

            var previous = blocks[b - 1][^1];
            var blockDays = block.Select(d => d.Date).ToList();
            var ideal = IdealSuccessor(
                previous.LastKindIndex,
                IsLongPause(previous, block[0]),
                kind => isAllowedOnDays(kind, blockDays));
            if (ideal is null)
            {
                continue;
            }

            transitions++;
            if (block[0].FirstKindIndex != ideal.Value)
            {
                nonIdeal++;
            }
        }

        return new RotationAssessment(steps, changes, transitions, nonIdeal);
    }
}
