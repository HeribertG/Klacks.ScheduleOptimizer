// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using System.Globalization;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Llm;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Candidates;

/// <summary>
/// Suggests same-day swaps that widen a short rest period (1-2 free days) between two
/// work blocks of one row. The candidate moves the work day directly adjacent to the gap
/// onto a colleague who is free that day — the target row gets a longer pause; the
/// colleague picks up one extra work day, which the score and committee will validate.
/// </summary>
public sealed class EnlargePauseCandidateGenerator : IMoveCandidateGenerator
{
    private const int MaxPauseLengthToWiden = 2;

    public string Intent => HolisticIntent.EnlargePause;

    public IEnumerable<MoveCandidate> Generate(HarmonyBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        for (var row = 0; row < bitmap.RowCount; row++)
        {
            var shortPauses = FindShortPauses(bitmap, row);
            foreach (var pause in shortPauses)
            {
                foreach (var edgeDay in EdgeWorkDays(pause))
                {
                    var edgeCell = bitmap.GetCell(row, edgeDay);
                    if (edgeCell.IsLocked || !IsWork(edgeCell.Symbol))
                    {
                        continue;
                    }

                    for (var partner = 0; partner < bitmap.RowCount; partner++)
                    {
                        if (partner == row)
                        {
                            continue;
                        }
                        var partnerCell = bitmap.GetCell(partner, edgeDay);
                        if (partnerCell.IsLocked || partnerCell.Symbol != CellSymbol.Free)
                        {
                            continue;
                        }

                        var hint = string.Format(
                            CultureInfo.InvariantCulture,
                            "widens r{0:D2} pause around days {1}-{2} (current length {3})",
                            row, pause.Start, pause.End, pause.Length);

                        yield return new MoveCandidate(
                            RowA: row,
                            DayA: edgeDay,
                            RowB: partner,
                            DayB: edgeDay,
                            Hint: hint,
                            ExpectedBenefit: MaxPauseLengthToWiden + 1 - pause.Length);
                    }
                }
            }
        }
    }

    private static List<Pause> FindShortPauses(HarmonyBitmap bitmap, int row)
    {
        var blocks = new List<(int Start, int End)>();
        int? blockStart = null;
        for (var d = 0; d < bitmap.DayCount; d++)
        {
            var isWork = IsWork(bitmap.GetCell(row, d).Symbol);
            if (isWork && blockStart is null)
            {
                blockStart = d;
            }
            else if (!isWork && blockStart is not null)
            {
                blocks.Add((blockStart.Value, d - 1));
                blockStart = null;
            }
        }
        if (blockStart is not null)
        {
            blocks.Add((blockStart.Value, bitmap.DayCount - 1));
        }

        var pauses = new List<Pause>();
        for (var i = 0; i < blocks.Count - 1; i++)
        {
            var pauseStart = blocks[i].End + 1;
            var pauseEnd = blocks[i + 1].Start - 1;
            var length = pauseEnd - pauseStart + 1;
            if (length <= 0 || length > MaxPauseLengthToWiden)
            {
                continue;
            }
            pauses.Add(new Pause(blocks[i].End, blocks[i + 1].Start, pauseStart, pauseEnd, length));
        }
        return pauses;
    }

    private static IEnumerable<int> EdgeWorkDays(Pause pause)
    {
        yield return pause.LeftBlockEnd;
        if (pause.LeftBlockEnd != pause.RightBlockStart)
        {
            yield return pause.RightBlockStart;
        }
    }

    private static bool IsWork(CellSymbol symbol)
        => symbol == CellSymbol.Early
        || symbol == CellSymbol.Late
        || symbol == CellSymbol.Night
        || symbol == CellSymbol.Other;

    private readonly record struct Pause(int LeftBlockEnd, int RightBlockStart, int Start, int End, int Length);
}
