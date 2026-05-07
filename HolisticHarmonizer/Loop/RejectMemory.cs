// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Loop;

/// <summary>
/// Bounded ring of recent batch rejections that the inner loop replays back to the LLM
/// in the next prompt so it stops repeating discarded ideas. Capacity is intentionally small
/// (5 entries) to keep prompt overhead low; oldest entries fall out as new ones arrive.
/// </summary>
/// <param name="capacity">Maximum number of entries kept; defaults to 5.</param>
public sealed class RejectMemory
{
    private const int DefaultCapacity = 5;

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
        _entries.AddLast(new RejectMemoryEntry(evaluation.Intent, evaluation.Result, summary));
        while (_entries.Count > _capacity)
        {
            _entries.RemoveFirst();
        }
    }

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
public sealed record RejectMemoryEntry(string Intent, BatchAcceptance Result, string Summary);
