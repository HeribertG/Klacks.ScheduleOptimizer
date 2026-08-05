// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Derives a diverse clone from an already built scenario by randomly omitting a small share of its
/// mutable tokens. The auction and coverage-first strategies are deterministic, so asking them for N
/// individuals produced N identical genomes - a population without variance the GA cannot work with.
/// The omission rates mirror <see cref="WarmStartTokenStrategy"/>, which uses the same idea to keep
/// diversity around the warm-start pattern. Locked tokens are never dropped: they are the immutable
/// cells of the plan and must appear in every individual.
/// </summary>
public static class ScenarioPerturbator
{
    /// <summary>Lower bound of the share of mutable tokens that are randomly omitted.</summary>
    private const double MinPerturbationRate = 0.05;

    /// <summary>Upper bound of the share of mutable tokens that are randomly omitted.</summary>
    private const double MaxPerturbationRate = 0.15;

    /// <summary>
    /// Builds a new scenario from <paramref name="source"/> with a random share of its mutable tokens
    /// omitted. At least one mutable token is always dropped so the clone differs from its base.
    /// </summary>
    /// <param name="source">Scenario the clone is derived from; left untouched.</param>
    /// <param name="rng">Random source of the population build.</param>
    public static CoreScenario Perturb(CoreScenario source, Random rng)
    {
        var rate = MinPerturbationRate + (rng.NextDouble() * (MaxPerturbationRate - MinPerturbationRate));

        var kept = new List<CoreToken>(source.Tokens.Count);
        var keptMutableIndexes = new List<int>();
        var droppedAny = false;

        foreach (var token in source.Tokens)
        {
            if (token.IsLocked)
            {
                kept.Add(token);
                continue;
            }

            if (rng.NextDouble() < rate)
            {
                droppedAny = true;
                continue;
            }

            keptMutableIndexes.Add(kept.Count);
            kept.Add(token);
        }

        if (!droppedAny && keptMutableIndexes.Count > 0)
        {
            kept.RemoveAt(keptMutableIndexes[rng.Next(keptMutableIndexes.Count)]);
        }

        return new CoreScenario
        {
            Id = Guid.NewGuid().ToString(),
            Tokens = kept,
        };
    }
}
