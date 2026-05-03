// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Scorer;

namespace Klacks.ScheduleOptimizer.Harmonizer.Conductor;

/// <summary>
/// Generates and evaluates same-day swap candidates for a given primary row. Acceptance is
/// asymmetric: the primary row must gain harmony, and the partner row's loss is weighted by
/// row-rank so upper rows are protected (loss tolerated less when partner is above primary,
/// more when partner is below).
/// </summary>
public sealed class ReplaceMutation
{
    private readonly HarmonyScorer _scorer;
    private readonly IReplaceValidator _validator;
    private readonly int _candidatesPerInvocation;

    public ReplaceMutation(HarmonyScorer scorer, IReplaceValidator validator, int candidatesPerInvocation = 32)
    {
        _scorer = scorer;
        _validator = validator;
        _candidatesPerInvocation = candidatesPerInvocation;
    }

    public ReplaceOutcome FindBestMove(HarmonyBitmap bitmap, int primaryRow, IReadOnlySet<int> lockedRows)
    {
        var primaryScoreBefore = _scorer.Score(bitmap, primaryRow).Score;
        ReplaceMove? bestMove = null;
        var bestNetGain = double.NegativeInfinity;
        var bestPrimaryDelta = 0.0;
        var bestPartnerDelta = 0.0;

        var candidates = EnumerateCandidates(bitmap, primaryRow, lockedRows);
        foreach (var move in candidates)
        {
            if (!_validator.IsValid(bitmap, move))
            {
                continue;
            }

            var partnerScoreBefore = _scorer.Score(bitmap, move.RowB).Score;
            ApplySwap(bitmap, move);

            var primaryScoreAfter = _scorer.Score(bitmap, primaryRow).Score;
            var partnerScoreAfter = _scorer.Score(bitmap, move.RowB).Score;
            var primaryDelta = primaryScoreAfter - primaryScoreBefore;
            var partnerDelta = partnerScoreAfter - partnerScoreBefore;

            ApplySwap(bitmap, move);

            if (primaryDelta <= 0)
            {
                continue;
            }

            var weight = AsymmetricWeight(primaryRow, move.RowB);
            var netGain = primaryDelta + weight * partnerDelta;
            if (netGain <= 0)
            {
                continue;
            }

            if (netGain > bestNetGain)
            {
                bestMove = move;
                bestNetGain = netGain;
                bestPrimaryDelta = primaryDelta;
                bestPartnerDelta = partnerDelta;
            }
        }

        return new ReplaceOutcome(bestMove, bestPrimaryDelta, bestPartnerDelta, bestNetGain);
    }

    public void Apply(HarmonyBitmap bitmap, ReplaceMove move) => ApplySwap(bitmap, move);

    private IEnumerable<ReplaceMove> EnumerateCandidates(HarmonyBitmap bitmap, int primaryRow, IReadOnlySet<int> lockedRows)
    {
        var produced = 0;
        for (var day = 0; day < bitmap.DayCount && produced < _candidatesPerInvocation; day++)
        {
            for (var partner = 0; partner < bitmap.RowCount && produced < _candidatesPerInvocation; partner++)
            {
                if (partner == primaryRow)
                {
                    continue;
                }
                if (lockedRows.Contains(partner))
                {
                    continue;
                }
                var cellPrimary = bitmap.GetCell(primaryRow, day);
                var cellPartner = bitmap.GetCell(partner, day);
                if (cellPrimary.Symbol == cellPartner.Symbol)
                {
                    continue;
                }
                yield return new ReplaceMove(primaryRow, partner, day);
                produced++;
            }
        }
    }

    private static void ApplySwap(HarmonyBitmap bitmap, ReplaceMove move)
    {
        var cellA = bitmap.GetCell(move.RowA, move.Day);
        var cellB = bitmap.GetCell(move.RowB, move.Day);
        bitmap.SetCell(move.RowA, move.Day, cellB);
        bitmap.SetCell(move.RowB, move.Day, cellA);
    }

    private static double AsymmetricWeight(int primaryRow, int partnerRow)
    {
        if (partnerRow > primaryRow)
        {
            return 1.0 / (primaryRow + 1);
        }
        return (double)(partnerRow + 1) / (primaryRow + 1);
    }
}

/// <param name="Move">The selected move, or null if no move improved harmony</param>
/// <param name="PrimaryDelta">Change in the primary row's harmony score (always &gt; 0 when Move is non-null)</param>
/// <param name="PartnerDelta">Change in the partner row's score (negative = partner row lost harmony)</param>
/// <param name="NetGain">primaryDelta + weight * partnerDelta — positive means accepted</param>
public sealed record ReplaceOutcome(ReplaceMove? Move, double PrimaryDelta, double PartnerDelta, double NetGain);
