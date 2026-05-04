// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Scorer;

namespace Klacks.ScheduleOptimizer.Harmonizer.Conductor;

/// <summary>
/// Generates and evaluates multi-day block-swap candidates between two rows. A block is a
/// contiguous run of non-Free cells in the primary row; the mutation looks for an equally-long,
/// non-Free run in another row and tries swapping the two block ranges as a unit. This is the
/// orthogonal mutation to <see cref="ReplaceMutation"/> (single-day swaps): block swaps preserve
/// intra-block homogeneity while creating shift-type rotation across blocks that single-day
/// swaps cannot achieve in a single accepted step.
/// </summary>
public sealed class BlockSwapMutation
{
    private readonly HarmonyScorer _scorer;
    private readonly IReplaceValidator _validator;
    private readonly int _maxCandidatesPerInvocation;

    public BlockSwapMutation(HarmonyScorer scorer, IReplaceValidator validator, int maxCandidatesPerInvocation = 32)
    {
        _scorer = scorer;
        _validator = validator;
        _maxCandidatesPerInvocation = maxCandidatesPerInvocation;
    }

    public BlockSwapOutcome FindBestMove(HarmonyBitmap bitmap, int primaryRow, IReadOnlySet<int> lockedRows)
    {
        var primaryScoreBefore = _scorer.Score(bitmap, primaryRow).Score;
        BlockSwapMove? bestMove = null;
        var bestNetGain = double.NegativeInfinity;
        var bestPrimaryDelta = 0.0;
        var bestPartnerDelta = 0.0;

        var produced = 0;
        var primaryBlocks = ScanBlocks(bitmap, primaryRow);
        foreach (var block in primaryBlocks)
        {
            if (produced >= _maxCandidatesPerInvocation)
            {
                break;
            }

            for (var partnerRow = 0; partnerRow < bitmap.RowCount; partnerRow++)
            {
                if (partnerRow == primaryRow || lockedRows.Contains(partnerRow))
                {
                    continue;
                }
                if (!IsRangeFullyAssigned(bitmap, partnerRow, block.StartDay, block.Length))
                {
                    continue;
                }
                if (HasSameSymbols(bitmap, primaryRow, partnerRow, block.StartDay, block.Length))
                {
                    continue;
                }

                var move = new BlockSwapMove(primaryRow, partnerRow, block.StartDay, block.Length);
                if (!ValidateBlockSwap(bitmap, move))
                {
                    continue;
                }

                var partnerScoreBefore = _scorer.Score(bitmap, partnerRow).Score;
                ApplySwap(bitmap, move);
                var primaryScoreAfter = _scorer.Score(bitmap, primaryRow).Score;
                var partnerScoreAfter = _scorer.Score(bitmap, partnerRow).Score;
                var primaryDelta = primaryScoreAfter - primaryScoreBefore;
                var partnerDelta = partnerScoreAfter - partnerScoreBefore;
                ApplySwap(bitmap, move);

                produced++;
                if (primaryDelta <= 0)
                {
                    continue;
                }

                var weight = AsymmetricWeight(primaryRow, partnerRow);
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

                if (produced >= _maxCandidatesPerInvocation)
                {
                    break;
                }
            }
        }

        return new BlockSwapOutcome(bestMove, bestPrimaryDelta, bestPartnerDelta, bestNetGain);
    }

    public void Apply(HarmonyBitmap bitmap, BlockSwapMove move) => ApplySwap(bitmap, move);

    private bool ValidateBlockSwap(HarmonyBitmap bitmap, BlockSwapMove move)
    {
        var applied = 0;
        for (var offset = 0; offset < move.Length; offset++)
        {
            var day = move.StartDay + offset;
            var perDay = new ReplaceMove(move.RowA, move.RowB, day);
            if (!_validator.IsValid(bitmap, perDay))
            {
                UndoPartial(bitmap, move, applied);
                return false;
            }
            SwapDay(bitmap, move.RowA, move.RowB, day);
            applied++;
        }

        UndoPartial(bitmap, move, applied);
        return true;
    }

    private static void UndoPartial(HarmonyBitmap bitmap, BlockSwapMove move, int applied)
    {
        for (var offset = 0; offset < applied; offset++)
        {
            SwapDay(bitmap, move.RowA, move.RowB, move.StartDay + offset);
        }
    }

    private static void SwapDay(HarmonyBitmap bitmap, int rowA, int rowB, int day)
    {
        var cellA = bitmap.GetCell(rowA, day);
        var cellB = bitmap.GetCell(rowB, day);
        bitmap.SetCell(rowA, day, cellB);
        bitmap.SetCell(rowB, day, cellA);
    }

    private static void ApplySwap(HarmonyBitmap bitmap, BlockSwapMove move)
    {
        for (var offset = 0; offset < move.Length; offset++)
        {
            var day = move.StartDay + offset;
            var cellA = bitmap.GetCell(move.RowA, day);
            var cellB = bitmap.GetCell(move.RowB, day);
            bitmap.SetCell(move.RowA, day, cellB);
            bitmap.SetCell(move.RowB, day, cellA);
        }
    }

    private static List<Block> ScanBlocks(HarmonyBitmap bitmap, int rowIndex)
    {
        var blocks = new List<Block>();
        var blockStart = -1;
        for (var d = 0; d < bitmap.DayCount; d++)
        {
            var cell = bitmap.GetCell(rowIndex, d);
            if (cell.Symbol != CellSymbol.Free && !cell.IsLocked)
            {
                if (blockStart < 0)
                {
                    blockStart = d;
                }
                continue;
            }
            if (blockStart >= 0)
            {
                blocks.Add(new Block(blockStart, d - blockStart));
                blockStart = -1;
            }
        }
        if (blockStart >= 0)
        {
            blocks.Add(new Block(blockStart, bitmap.DayCount - blockStart));
        }
        return blocks;
    }

    private static bool IsRangeFullyAssigned(HarmonyBitmap bitmap, int rowIndex, int startDay, int length)
    {
        for (var offset = 0; offset < length; offset++)
        {
            var cell = bitmap.GetCell(rowIndex, startDay + offset);
            if (cell.Symbol == CellSymbol.Free || cell.IsLocked)
            {
                return false;
            }
        }
        return true;
    }

    private static bool HasSameSymbols(HarmonyBitmap bitmap, int rowA, int rowB, int startDay, int length)
    {
        for (var offset = 0; offset < length; offset++)
        {
            var symA = bitmap.GetCell(rowA, startDay + offset).Symbol;
            var symB = bitmap.GetCell(rowB, startDay + offset).Symbol;
            if (symA != symB)
            {
                return false;
            }
        }
        return true;
    }

    private static double AsymmetricWeight(int primaryRow, int partnerRow)
    {
        if (partnerRow > primaryRow)
        {
            return 1.0 / (primaryRow + 1);
        }
        return (double)(partnerRow + 1) / (primaryRow + 1);
    }

    private readonly record struct Block(int StartDay, int Length);
}

/// <param name="Move">The selected block swap, or null if no candidate improved harmony</param>
/// <param name="PrimaryDelta">Change in the primary row's harmony score (always &gt; 0 when Move is non-null)</param>
/// <param name="PartnerDelta">Change in the partner row's score (negative = partner row lost harmony)</param>
/// <param name="NetGain">primaryDelta + weight * partnerDelta — positive means accepted</param>
public sealed record BlockSwapOutcome(BlockSwapMove? Move, double PrimaryDelta, double PartnerDelta, double NetGain);
