// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Loop;

/// <summary>
/// Picks one intent per inner-loop iteration with a roulette-wheel weighted by Laplace-smoothed
/// success rates from <see cref="IntentSuccessTracker"/>. A small floor weight prevents any
/// intent from starving — even an intent with zero accepts so far gets occasional exploration.
/// Random source is injectable so tests can run deterministically.
/// </summary>
/// <param name="random">Random source. Defaults to a fresh non-seeded instance.</param>
public sealed class RouletteIntentSelector
{
    private const double MinWeightFloor = 0.05;

    private readonly Random _random;

    public RouletteIntentSelector(Random? random = null)
    {
        _random = random ?? new Random();
    }

    public string Pick(IReadOnlyList<string> intents, IntentSuccessTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(intents);
        ArgumentNullException.ThrowIfNull(tracker);
        if (intents.Count == 0)
        {
            throw new ArgumentException("At least one intent required.", nameof(intents));
        }

        var weights = new double[intents.Count];
        var total = 0.0;
        for (var i = 0; i < intents.Count; i++)
        {
            var weight = Math.Max(MinWeightFloor, tracker.SuccessRate(intents[i]));
            weights[i] = weight;
            total += weight;
        }

        var roll = _random.NextDouble() * total;
        var cumulative = 0.0;
        for (var i = 0; i < intents.Count; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
            {
                return intents[i];
            }
        }

        return intents[^1];
    }
}
