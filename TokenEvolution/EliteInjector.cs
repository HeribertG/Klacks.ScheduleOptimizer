// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution;

/// <summary>
/// Places an improved scenario back into the population by overwriting its weakest member. Without this
/// the coverage sweep only ever improves the reported best - the next generation still breeds from the
/// unswept population, so the gain is lost as soon as another individual takes the lead.
/// </summary>
public static class EliteInjector
{
    /// <summary>
    /// Replaces the worst individual of the population with the given elite. No-op on an empty population.
    /// </summary>
    /// <param name="population">The current population; modified in place.</param>
    /// <param name="elite">The scenario to inject; must already carry evaluated fitness values.</param>
    /// <param name="comparer">Ranking comparer; a negative result means the first argument is better.</param>
    public static void ReplaceWorst(List<CoreScenario> population, CoreScenario elite, IComparer<CoreScenario> comparer)
    {
        if (population.Count == 0)
        {
            return;
        }

        var worstIndex = 0;
        for (var i = 1; i < population.Count; i++)
        {
            if (comparer.Compare(population[i], population[worstIndex]) > 0)
            {
                worstIndex = i;
            }
        }

        population[worstIndex] = elite;
    }
}
