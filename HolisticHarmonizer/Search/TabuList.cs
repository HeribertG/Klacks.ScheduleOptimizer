// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.HolisticHarmonizer.Loop;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Search;

/// <summary>
/// Short-term memory of recently applied swaps for the deterministic stage-3 search. Swapping the same two
/// cells on the same day right after the swap is the exact reverse move, so a key stays forbidden for
/// <c>tenure</c> iterations after it was applied. The optimizer only applies the ban to non-improving moves
/// (aspiration criterion): once other moves touched the same cells, the coordinates describe a new move. Keys use
/// the reject-memory form <see cref="ForbiddenSwapKey"/>. Only same-day swaps are recorded:
/// the key drops DayB, and a cross-day swap must not ban an unrelated same-day candidate.
/// </summary>
/// <param name="tenure">Number of iterations a key stays tabu; 0 disables the list.</param>
public sealed class TabuList
{
    private readonly int _tenure;
    private readonly Dictionary<ForbiddenSwapKey, int> _expiresAfter = new();

    public TabuList(int tenure)
    {
        if (tenure < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tenure), "Tenure must not be negative.");
        }
        _tenure = tenure;
    }

    public int Count => _expiresAfter.Count;

    /// <summary>Records the swaps applied in <paramref name="iteration"/>; they stay tabu through iteration + tenure.</summary>
    public void Record(IEnumerable<PlanCellSwap> appliedSwaps, int iteration)
    {
        ArgumentNullException.ThrowIfNull(appliedSwaps);
        if (_tenure == 0)
        {
            return;
        }
        foreach (var swap in appliedSwaps)
        {
            if (swap.DayA == swap.DayB)
            {
                _expiresAfter[ForbiddenSwapKey.From(swap)] = iteration + _tenure;
            }
        }
    }

    /// <summary>Keys that are tabu in <paramref name="iteration"/>; expired keys are dropped.</summary>
    public IReadOnlySet<ForbiddenSwapKey> ActiveKeys(int iteration)
    {
        var expired = _expiresAfter.Where(e => e.Value < iteration).Select(e => e.Key).ToList();
        foreach (var key in expired)
        {
            _expiresAfter.Remove(key);
        }
        return new HashSet<ForbiddenSwapKey>(_expiresAfter.Keys);
    }

    public bool IsTabu(PlanCellSwap swap, int iteration)
    {
        ArgumentNullException.ThrowIfNull(swap);
        return swap.DayA == swap.DayB
            && _expiresAfter.TryGetValue(ForbiddenSwapKey.From(swap), out var expiresAfter)
            && expiresAfter >= iteration;
    }
}
