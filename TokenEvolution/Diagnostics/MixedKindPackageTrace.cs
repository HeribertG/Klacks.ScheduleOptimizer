// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Diagnostics;

/// <summary>
/// Counts the packages that change their shift kind inside the package — the quantity rule 7 asks to be
/// zero. A package is a run of consecutive worked days of one agent and a day is represented by the kind
/// of its earliest shift, so a permitted split duty on one day is not read as a kind change; that is the
/// same reading the block-ordering term of the fitness uses.
/// <para>
/// The count exists on its own because the fitness folds rule 7 and the rotation of rule 8 into a single
/// block-ordering number. A move can therefore buy rotation with package constancy without that number
/// falling — a trade the priority order forbids, rule 7 standing above rule 8 — and only a separate
/// counter can refuse it.
/// </para>
/// </summary>
public static class MixedKindPackageTrace
{
    /// <summary>Number of packages of the plan that hold more than one shift kind.</summary>
    /// <param name="scenario">Plan to measure</param>
    public static int Count(CoreScenario scenario)
    {
        var kindsByAgent = new Dictionary<string, SortedDictionary<DateOnly, int>>(StringComparer.Ordinal);
        foreach (var token in scenario.Tokens.OrderBy(t => t.Date).ThenBy(t => t.StartAt))
        {
            if (!kindsByAgent.TryGetValue(token.AgentId, out var byDay))
            {
                byDay = [];
                kindsByAgent[token.AgentId] = byDay;
            }

            byDay.TryAdd(token.Date, token.ShiftTypeIndex);
        }

        var mixed = 0;
        foreach (var byDay in kindsByAgent.Values)
        {
            DateOnly? previous = null;
            var kindOfRun = 0;
            var runIsMixed = false;

            foreach (var (day, kind) in byDay)
            {
                if (previous is null || day.DayNumber - previous.Value.DayNumber > 1)
                {
                    if (runIsMixed)
                    {
                        mixed++;
                    }

                    kindOfRun = kind;
                    runIsMixed = false;
                }
                else if (kind != kindOfRun)
                {
                    runIsMixed = true;
                }

                previous = day;
            }

            if (runIsMixed)
            {
                mixed++;
            }
        }

        return mixed;
    }
}
