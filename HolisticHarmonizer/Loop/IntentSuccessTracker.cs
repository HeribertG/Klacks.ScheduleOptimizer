// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.HolisticHarmonizer.Llm;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Loop;

/// <summary>
/// Per-run tally of how often each intent has been proposed and how often the resulting batch
/// was accepted. Feeds <see cref="RouletteIntentSelector"/> so winning intents get more
/// prompt-focus on later iterations. Counts are Laplace-smoothed — every intent starts with a
/// neutral 0.5 success-rate so the selector cannot starve a new intent on the very first pick.
/// </summary>
public sealed class IntentSuccessTracker
{
    private readonly Dictionary<string, IntentTally> _tallies;

    public IntentSuccessTracker()
        : this(HolisticIntent.All)
    {
    }

    public IntentSuccessTracker(IEnumerable<string> intents)
    {
        ArgumentNullException.ThrowIfNull(intents);
        _tallies = new Dictionary<string, IntentTally>(StringComparer.Ordinal);
        foreach (var intent in intents)
        {
            _tallies[intent] = new IntentTally();
        }
    }

    /// <summary>
    /// Record one observed batch outcome. <paramref name="accepted"/> covers both
    /// <see cref="BatchAcceptance.Accepted"/> and <see cref="BatchAcceptance.PartiallyAccepted"/> — partial
    /// progress still counts as a win for the intent, only a full hard- or score-rejection is a loss.
    /// </summary>
    public void Note(string intent, BatchAcceptance result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);
        if (!_tallies.TryGetValue(intent, out var tally))
        {
            tally = new IntentTally();
            _tallies[intent] = tally;
        }

        tally.ProposedCount++;
        if (result == BatchAcceptance.Accepted || result == BatchAcceptance.PartiallyAccepted)
        {
            tally.AcceptedCount++;
        }
    }

    /// <summary>
    /// Laplace-smoothed success rate in (0, 1). Neutral 0.5 with no data; converges to the
    /// observed accept-rate with a single fictive accept and a single fictive reject baked in.
    /// </summary>
    public double SuccessRate(string intent)
    {
        if (!_tallies.TryGetValue(intent, out var tally))
        {
            return 0.5;
        }
        return (tally.AcceptedCount + 1.0) / (tally.ProposedCount + 2.0);
    }

    /// <summary>Diagnostic snapshot of all tracked intents — used by logging and tests.</summary>
    public IReadOnlyDictionary<string, (int Proposed, int Accepted)> Snapshot()
        => _tallies.ToDictionary(p => p.Key, p => (p.Value.ProposedCount, p.Value.AcceptedCount));

    private sealed class IntentTally
    {
        public int ProposedCount;
        public int AcceptedCount;
    }
}
