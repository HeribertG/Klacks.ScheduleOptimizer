// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.TokenEvolution;

/// <summary>
/// Accepted moves of the keyword-wish repairs in one phase of a run (evolution or end passes), per repair kind, so the
/// run's trace shows whether the single exchange (SingleSwaps, WishRepairSwap) or the block move (BlockMoves,
/// WishBlockRepair) did the work.
/// </summary>
internal sealed class WishRepairTally
{
    public int SingleSwaps { get; private set; }

    public int BlockMoves { get; private set; }

    public void Add(int singleSwaps, int blockMoves)
    {
        SingleSwaps += singleSwaps;
        BlockMoves += blockMoves;
    }

    public string Describe(string phase)
        => $"Run: wish repair {phase}: accepted single swaps={SingleSwaps}, accepted block moves={BlockMoves}";
}
