// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Wizard3.Loop;

/// <summary>
/// Tracks the maximum number of steps the LLM may put into a single batch. Grows on accepted
/// batches (the LLM is on track, let it try larger holistic moves), shrinks on rejects
/// (something is off, reduce blast radius and let the LLM retry with smaller scope).
/// </summary>
/// <param name="initial">Starting cap (defaults to 3 — small enough to be safe, big enough
/// to demonstrate the holistic-batch advantage over Wizard 2's single swaps).</param>
/// <param name="minimum">Lower bound (defaults to 1 — never below a single step).</param>
/// <param name="maximum">Upper bound (defaults to 8 — more than ~8 atomic swaps is usually a
/// sign the LLM is stitching unrelated moves together).</param>
public sealed class AdaptiveBatchCap
{
    private const int DefaultInitial = 3;
    private const int DefaultMinimum = 1;
    private const int DefaultMaximum = 8;

    private readonly int _minimum;
    private readonly int _maximum;
    private int _current;

    public AdaptiveBatchCap(int initial = DefaultInitial, int minimum = DefaultMinimum, int maximum = DefaultMaximum)
    {
        if (minimum < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), "Minimum must be >= 1.");
        }
        if (maximum < minimum)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum), "Maximum must be >= minimum.");
        }
        if (initial < minimum || initial > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(initial), "Initial must lie in [minimum, maximum].");
        }
        _minimum = minimum;
        _maximum = maximum;
        _current = initial;
    }

    public int Current => _current;

    public void RecordAccept()
    {
        if (_current < _maximum)
        {
            _current++;
        }
    }

    public void RecordReject()
    {
        if (_current > _minimum)
        {
            _current--;
        }
    }
}
