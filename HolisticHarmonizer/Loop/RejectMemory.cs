// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Loop;

/// <summary>
/// Bounded ring of recent batch rejections that the inner loop replays back to the LLM
/// in the next prompt so it stops repeating discarded ideas. Capacity defaults to 10 entries
/// — large enough to cover a full inner-loop run (<c>MaxInnerIterations = 10</c> with up to
/// 3 batches each) without the prompt overhead exploding. Oldest entries fall out as new ones arrive.
/// </summary>
/// <param name="capacity">Maximum number of entries kept; defaults to 10.</param>
public sealed class RejectMemory
{
    private const int DefaultCapacity = 10;

    private readonly int _capacity;
    private readonly LinkedList<RejectMemoryEntry> _entries = new();

    public RejectMemory(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be >= 1.");
        }
        _capacity = capacity;
    }

    public IReadOnlyCollection<RejectMemoryEntry> Entries => _entries;

    public void Note(BatchEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        if (evaluation.Result == BatchAcceptance.Accepted)
        {
            return;
        }

        var summary = BuildSummary(evaluation);
        var rejectedSwaps = ExtractRejectedSwaps(evaluation);
        _entries.AddLast(new RejectMemoryEntry(evaluation.Intent, evaluation.Result, summary, rejectedSwaps));
        while (_entries.Count > _capacity)
        {
            _entries.RemoveFirst();
        }
    }

    /// <summary>
    /// Returns the de-duplicated set of swap coordinates that the LLM should NOT repeat,
    /// in canonical form (smaller row first, then smaller day). Used by the prompt builder
    /// to emit a compact "DO NOT REPEAT THESE EXACT SWAPS" list per iteration.
    /// </summary>
    public IReadOnlyList<ForbiddenSwapKey> ForbiddenSwapKeys()
    {
        var seen = new HashSet<ForbiddenSwapKey>();
        var ordered = new List<ForbiddenSwapKey>();
        foreach (var entry in _entries)
        {
            for (var i = 0; i < entry.RejectedSwaps.Count; i++)
            {
                var key = ForbiddenSwapKey.From(entry.RejectedSwaps[i]);
                if (seen.Add(key))
                {
                    ordered.Add(key);
                }
            }
        }
        return ordered;
    }

    private static IReadOnlyList<PlanCellSwap> ExtractRejectedSwaps(BatchEvaluation evaluation) =>
        evaluation.Result switch
        {
            BatchAcceptance.Rejected => evaluation.Rejections.Count > 0
                ? evaluation.Rejections.Select(r => r.Swap).ToList()
                : [],
            BatchAcceptance.PartiallyAccepted => evaluation.Rejections.Select(r => r.Swap).ToList(),
            BatchAcceptance.WouldDegrade => evaluation.AppliedSteps,
            _ => [],
        };

    private static string BuildSummary(BatchEvaluation evaluation)
    {
        switch (evaluation.Result)
        {
            case BatchAcceptance.WouldDegrade:
                return $"all steps passed hard constraints but final score {evaluation.ScoreAfter:F3} <= start {evaluation.ScoreBefore:F3}";
            case BatchAcceptance.PartiallyAccepted:
                return $"prefix of {evaluation.AppliedSteps.Count} step(s) kept; rest broke at step {evaluation.StoppedAtStep}";
            case BatchAcceptance.Rejected when evaluation.Rejections.Count > 0:
                var rej = evaluation.Rejections[0];
                var swap = rej.Swap;
                return $"step {evaluation.StoppedAtStep ?? 0} (rowA={swap.RowA} dayA={swap.DayA} ↔ rowB={swap.RowB} dayB={swap.DayB}) {rej.Reason}: {rej.Detail}";
            case BatchAcceptance.Rejected:
                return "no valid step";
            default:
                return string.Empty;
        }
    }
}

/// <param name="Intent">Intent label of the rejected batch.</param>
/// <param name="Result">Acceptance category that triggered the memory entry (never Accepted).</param>
/// <param name="Summary">Compact human-readable cause for the LLM prompt.</param>
/// <param name="RejectedSwaps">Concrete swap coordinates that did not survive evaluation;
/// surfaced in the next prompt as a DO-NOT-REPEAT list so the LLM stops re-emitting them.</param>
public sealed record RejectMemoryEntry(
    string Intent,
    BatchAcceptance Result,
    string Summary,
    IReadOnlyList<PlanCellSwap> RejectedSwaps);

/// <summary>
/// Canonical key for a swap coordinate pair (rowA, dayA, rowB, dayB), normalized so the
/// pair (a, b) and (b, a) hash to the same key. Day must match (same-day swap invariant)
/// so it is stored once. Used by reject memory to deduplicate forbidden-swap entries.
/// </summary>
public readonly record struct ForbiddenSwapKey(int RowSmaller, int RowLarger, int Day)
{
    public static ForbiddenSwapKey From(PlanCellSwap swap)
    {
        ArgumentNullException.ThrowIfNull(swap);
        var (smaller, larger) = swap.RowA <= swap.RowB ? (swap.RowA, swap.RowB) : (swap.RowB, swap.RowA);
        return new ForbiddenSwapKey(smaller, larger, swap.DayA);
    }
}
