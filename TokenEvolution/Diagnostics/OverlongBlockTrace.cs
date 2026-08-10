// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using System.Globalization;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Diagnostics;

/// <summary>
/// Attributes overlong work packages to the evolution step that created them. A package is a maximal
/// run of consecutive worked calendar dates of one agent — the same definition the specification
/// measures — and it counts as overlong once it is longer than that agent's MaxWorkDays block ideal.
/// Comparing the packages of the plan before a step with the plan after it turns "the result carries
/// a six-day block" into "this operator built it", which no fitness value can answer: block length
/// only reaches the lexicographic comparison through the stage-4 symmetry term.
/// <para>
/// The whole computation is switched off by a null sink, so the production path pays a null check per
/// evolution step and nothing else.
/// </para>
/// </summary>
public static class OverlongBlockTrace
{
    private const string LinePrefix = "OVERLONG";

    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Reports every overlong package the step added, one line per package.
    /// </summary>
    /// <param name="sink">Diagnostics sink; null switches the comparison off entirely</param>
    /// <param name="stage">Name of the evolution step between the two plans</param>
    /// <param name="before">Plan the step started from; null reports every overlong package as new</param>
    /// <param name="after">Plan the step produced</param>
    /// <param name="context">Wizard context supplying MaxWorkDays and the works outside the genome</param>
    public static void Report(
        Action<string>? sink,
        string stage,
        CoreScenario? before,
        CoreScenario after,
        CoreWizardContext context)
        => Report(sink, stage, before, null, after, context);

    /// <summary>
    /// Reports every overlong package a two-parent step added, one line per package. A package that
    /// either parent already carried is not attributed to the step that recombined them.
    /// </summary>
    /// <param name="sink">Diagnostics sink; null switches the comparison off entirely</param>
    /// <param name="stage">Name of the evolution step between the plans</param>
    /// <param name="firstParent">First plan the step started from; null reports every overlong package as new</param>
    /// <param name="secondParent">Second plan the step started from, or null when the step has one input</param>
    /// <param name="after">Plan the step produced</param>
    /// <param name="context">Wizard context supplying MaxWorkDays and the works outside the genome</param>
    public static void Report(
        Action<string>? sink,
        string stage,
        CoreScenario? firstParent,
        CoreScenario? secondParent,
        CoreScenario after,
        CoreWizardContext context)
    {
        if (sink is null)
        {
            return;
        }

        var added = Overlong(after, context);
        if (added.Count == 0)
        {
            return;
        }

        if (firstParent is not null)
        {
            added.ExceptWith(Overlong(firstParent, context));
        }

        if (secondParent is not null)
        {
            added.ExceptWith(Overlong(secondParent, context));
        }

        foreach (var package in added.OrderBy(p => p, StringComparer.Ordinal))
        {
            sink($"{LinePrefix} {stage} {package}");
        }
    }

    /// <summary>
    /// All overlong packages of one plan, as sortable "agent start..end len" keys.
    /// </summary>
    /// <param name="scenario">Plan to measure</param>
    /// <param name="context">Wizard context supplying MaxWorkDays and the works outside the genome</param>
    public static HashSet<string> Overlong(CoreScenario scenario, CoreWizardContext context)
    {
        var capByAgent = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var agent in context.Agents)
        {
            capByAgent[agent.Id] = agent.MaxWorkDays;
        }

        var datesByAgent = new Dictionary<string, SortedSet<DateOnly>>(StringComparer.Ordinal);
        foreach (var token in scenario.Tokens)
        {
            Remember(datesByAgent, token.AgentId, token.Date);
        }

        foreach (var locked in context.BoundaryLockedWorks)
        {
            Remember(datesByAgent, locked.AgentId, locked.Date);
        }

        foreach (var blocker in context.BoundaryExistingWorkBlockers)
        {
            Remember(datesByAgent, blocker.AgentId, blocker.Date);
        }

        var overlong = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (agentId, dates) in datesByAgent)
        {
            var cap = capByAgent.GetValueOrDefault(agentId);
            if (cap <= 0)
            {
                continue;
            }

            foreach (var run in Runs(dates))
            {
                var length = run.End.DayNumber - run.Start.DayNumber + 1;
                if (length > cap)
                {
                    overlong.Add(Key(agentId, run.Start, run.End, length));
                }
            }
        }

        return overlong;
    }

    private static void Remember(
        Dictionary<string, SortedSet<DateOnly>> datesByAgent, string agentId, DateOnly date)
    {
        if (!datesByAgent.TryGetValue(agentId, out var dates))
        {
            dates = [];
            datesByAgent[agentId] = dates;
        }

        dates.Add(date);
    }

    private static IEnumerable<(DateOnly Start, DateOnly End)> Runs(SortedSet<DateOnly> dates)
    {
        DateOnly? start = null;
        DateOnly previous = default;

        foreach (var date in dates)
        {
            if (start is null)
            {
                start = date;
                previous = date;
                continue;
            }

            if (date.DayNumber - previous.DayNumber > 1)
            {
                yield return (start.Value, previous);
                start = date;
            }

            previous = date;
        }

        if (start is not null)
        {
            yield return (start.Value, previous);
        }
    }

    private static string Key(string agentId, DateOnly start, DateOnly end, int length)
        => string.Concat(
            agentId,
            " ",
            start.ToString(DateFormat, CultureInfo.InvariantCulture),
            "..",
            end.ToString(DateFormat, CultureInfo.InvariantCulture),
            " len=",
            length.ToString(CultureInfo.InvariantCulture));
}
