// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Deterministic random number generator using Linear Congruential Generator.
/// Mirrors createSeededRng from evolution-core.ts for reproducible results.
/// </summary>
/// <param name="seed">Initial seed value</param>

namespace Klacks.ScheduleOptimizer.Engine;

public class SeededRng
{
    private uint _state;

    public SeededRng(int seed)
    {
        _state = (uint)seed;
    }

    public double Next()
    {
        unchecked
        {
            _state = _state * EvolutionConstants.RNG_MULTIPLIER + EvolutionConstants.RNG_INCREMENT;
        }
        return _state / 4294967296.0;
    }
}
