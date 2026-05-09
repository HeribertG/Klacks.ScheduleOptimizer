// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using System.Globalization;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Llm;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Candidates;

/// <summary>
/// Suggests same-day swaps that move a single work day from a row whose actual hours are
/// over its target onto a row whose actual hours are under its target. Only rows with a
/// non-zero target are considered (target == 0 means unconstrained, no signal). For each
/// over/under pair the generator scans days where the over row works and the under row is
/// free, ranks by combined deviation magnitude.
/// </summary>
public sealed class RedistributeLoadCandidateGenerator : IMoveCandidateGenerator
{
    /// <summary>
    /// Minimum absolute hours-deviation a row must show before it qualifies as over- or under-target.
    /// Set low (0.1h) so that plans already close to optimum still surface a few candidates rather
    /// than handing the LLM an empty list that nudges it toward hallucinating its own coordinates.
    /// </summary>
    private const decimal DeviationFloor = 0.1m;

    public string Intent => HolisticIntent.RedistributeLoad;

    public IEnumerable<MoveCandidate> Generate(HarmonyBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var deviations = ComputeDeviations(bitmap);
        var overRows = new List<RowDeviation>();
        var underRows = new List<RowDeviation>();
        foreach (var dev in deviations)
        {
            if (dev.Deviation > DeviationFloor)
            {
                overRows.Add(dev);
            }
            else if (dev.Deviation < -DeviationFloor)
            {
                underRows.Add(dev);
            }
        }
        if (overRows.Count == 0 || underRows.Count == 0)
        {
            yield break;
        }

        foreach (var over in overRows)
        {
            foreach (var under in underRows)
            {
                for (var day = 0; day < bitmap.DayCount; day++)
                {
                    var overCell = bitmap.GetCell(over.Row, day);
                    if (overCell.IsLocked || !IsWork(overCell.Symbol))
                    {
                        continue;
                    }
                    var underCell = bitmap.GetCell(under.Row, day);
                    if (underCell.IsLocked || underCell.Symbol != CellSymbol.Free)
                    {
                        continue;
                    }

                    var hint = string.Format(
                        CultureInfo.InvariantCulture,
                        "moves day {0} from r{1:D2} (+{2:F1}h over) to r{3:D2} ({4:F1}h under)",
                        day, over.Row, over.Deviation, under.Row, -under.Deviation);

                    yield return new MoveCandidate(
                        RowA: over.Row,
                        DayA: day,
                        RowB: under.Row,
                        DayB: day,
                        Hint: hint,
                        ExpectedBenefit: (double)(over.Deviation - under.Deviation));
                }
            }
        }
    }

    private static List<RowDeviation> ComputeDeviations(HarmonyBitmap bitmap)
    {
        var result = new List<RowDeviation>(bitmap.RowCount);
        for (var r = 0; r < bitmap.RowCount; r++)
        {
            var agent = bitmap.Rows[r];
            if (agent.TargetHours <= 0)
            {
                continue;
            }
            decimal actual = 0m;
            for (var d = 0; d < bitmap.DayCount; d++)
            {
                actual += bitmap.GetCell(r, d).Hours;
            }
            result.Add(new RowDeviation(r, actual - agent.TargetHours));
        }
        return result;
    }

    private static bool IsWork(CellSymbol symbol)
        => symbol == CellSymbol.Early
        || symbol == CellSymbol.Late
        || symbol == CellSymbol.Night
        || symbol == CellSymbol.Other;

    private readonly record struct RowDeviation(int Row, decimal Deviation);
}
