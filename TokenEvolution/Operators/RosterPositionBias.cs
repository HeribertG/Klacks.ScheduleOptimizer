// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// Roulette-wheel selection helper for GA operators that mutate slot count per agent.
/// Top-bias prefers agents earlier in the roster (intended top-down distribution),
/// bottom-bias prefers agents later in the roster (used when removing tokens so that
/// surplus slots are taken away from the bottom first).
/// </summary>
/// <param name="candidates">Selection pool — must contain at least one element.</param>
/// <param name="agentIdOf">Extracts the agent id from a candidate so the helper can find its roster position.</param>
/// <param name="roster">Authoritative ordered agent list; index 0 is top, last is bottom.</param>
/// <param name="rng">RNG instance owned by the caller for reproducibility.</param>
public static class RosterPositionBias
{
    public static T PickWithTopBias<T>(
        IReadOnlyList<T> candidates,
        Func<T, string> agentIdOf,
        IReadOnlyList<CoreAgent> roster,
        Random rng)
    {
        return Pick(candidates, agentIdOf, roster, rng, inverse: false);
    }

    public static T PickWithBottomBias<T>(
        IReadOnlyList<T> candidates,
        Func<T, string> agentIdOf,
        IReadOnlyList<CoreAgent> roster,
        Random rng)
    {
        return Pick(candidates, agentIdOf, roster, rng, inverse: true);
    }

    private static T Pick<T>(
        IReadOnlyList<T> candidates,
        Func<T, string> agentIdOf,
        IReadOnlyList<CoreAgent> roster,
        Random rng,
        bool inverse)
    {
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var n = roster.Count;
        var weights = new double[candidates.Count];
        var total = 0.0;
        for (var i = 0; i < candidates.Count; i++)
        {
            var pos = IndexOf(roster, agentIdOf(candidates[i]));
            if (pos < 0 || pos >= n)
            {
                pos = n - 1;
            }
            weights[i] = inverse ? (pos + 1) : (n - pos);
            total += weights[i];
        }

        if (total <= 0)
        {
            return candidates[rng.Next(candidates.Count)];
        }

        var pick = rng.NextDouble() * total;
        var cumulative = 0.0;
        for (var i = 0; i < candidates.Count; i++)
        {
            cumulative += weights[i];
            if (pick < cumulative)
            {
                return candidates[i];
            }
        }
        return candidates[^1];
    }

    private static int IndexOf(IReadOnlyList<CoreAgent> roster, string agentId)
    {
        for (var i = 0; i < roster.Count; i++)
        {
            if (roster[i].Id == agentId)
            {
                return i;
            }
        }
        return -1;
    }
}
