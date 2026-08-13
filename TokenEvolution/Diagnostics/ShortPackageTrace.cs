// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using System.Globalization;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Diagnostics;

/// <summary>
/// Attributes SHORT work packages — runs of at most two consecutive worked calendar dates of one
/// agent, boundary works included — to the evolution step that created or dissolved them. The
/// seed-splintering diagnosis of SPEC.md decision 13 showed the auction seeds are more compact than
/// the final plans, and no fitness stage carries package length at a rank the lexicographic
/// comparison rejects on, so the destroyer is invisible until a step-by-step comparison names it.
/// Every added short package is reported with a "+", every dissolved one with a "-", so the net
/// drift per step kind falls out of a simple aggregation over the lines.
/// <para>
/// The whole computation is switched off by a null sink, so the production path pays a null check
/// per evolution step and nothing else.
/// </para>
/// </summary>
public static class ShortPackageTrace
{
    private const string LinePrefix = "SHORTPKG";

    private const string AddedMarker = "+";

    private const string RemovedMarker = "-";

    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>Longest run in days that still counts as a short package.</summary>
    public const int ShortPackageMaxDays = 2;

    /// <summary>
    /// Reports every short package the step added ("+") or dissolved ("-"), one line per package.
    /// </summary>
    /// <param name="sink">Diagnostics sink; null switches the comparison off entirely</param>
    /// <param name="stage">Name of the evolution step between the two plans</param>
    /// <param name="before">Plan the step started from; null reports every short package as new</param>
    /// <param name="after">Plan the step produced</param>
    /// <param name="context">Wizard context supplying the works outside the genome</param>
    public static void Report(
        Action<string>? sink,
        string stage,
        CoreScenario? before,
        CoreScenario after,
        CoreWizardContext context)
        => Report(sink, stage, before, null, after, context);

    /// <summary>
    /// Reports the short packages a two-parent step added or dissolved. A package either parent
    /// already carried is not attributed to the step that recombined them; a package both parents
    /// carried that the child lost counts as dissolved.
    /// </summary>
    /// <param name="sink">Diagnostics sink; null switches the comparison off entirely</param>
    /// <param name="stage">Name of the evolution step between the plans</param>
    /// <param name="firstParent">First plan the step started from; null reports every short package as new</param>
    /// <param name="secondParent">Second plan the step started from, or null when the step has one input</param>
    /// <param name="after">Plan the step produced</param>
    /// <param name="context">Wizard context supplying the works outside the genome</param>
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

        var result = Short(after, context);
        var parents = firstParent is null
            ? []
            : Short(firstParent, context);
        if (secondParent is not null)
        {
            parents.IntersectWith(Short(secondParent, context));
        }

        foreach (var package in result.Except(parents).OrderBy(p => p, StringComparer.Ordinal))
        {
            sink($"{LinePrefix} {AddedMarker} {stage} {package}");
        }

        foreach (var package in parents.Except(result).OrderBy(p => p, StringComparer.Ordinal))
        {
            sink($"{LinePrefix} {RemovedMarker} {stage} {package}");
        }
    }

    /// <summary>Emits one line carrying the plan's short-package count under the given label.</summary>
    /// <param name="sink">Diagnostics sink; null switches the count off entirely</param>
    /// <param name="label">Label of the measured plan, e.g. "gen12.selected origin=crossover+mutation.swap"</param>
    /// <param name="scenario">Plan to measure</param>
    /// <param name="context">Wizard context supplying the works outside the genome</param>
    public static void ReportCount(
        Action<string>? sink, string label, CoreScenario scenario, CoreWizardContext context)
        => sink?.Invoke(
            $"{LinePrefix} {label} count={Short(scenario, context).Count.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>
    /// All short packages of one plan, as sortable "agent start..end len" keys. Boundary works count
    /// toward the runs exactly as in <see cref="OverlongBlockTrace"/>, so a period-start run that
    /// continues February work is not misread as short.
    /// </summary>
    /// <param name="scenario">Plan to measure</param>
    /// <param name="context">Wizard context supplying the works outside the genome</param>
    public static HashSet<string> Short(CoreScenario scenario, CoreWizardContext context)
    {
        var datesByAgent = DatesByAgent(scenario, context);
        var shortPackages = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (agentId, dates) in datesByAgent)
        {
            foreach (var run in Runs(dates))
            {
                var length = run.End.DayNumber - run.Start.DayNumber + 1;
                if (length <= ShortPackageMaxDays)
                {
                    shortPackages.Add(Key(agentId, run.Start, run.End, length));
                }
            }
        }

        return shortPackages;
    }

    /// <summary>
    /// Short and total calendar-package counts of one plan — the fitness reading of
    /// <see cref="Short"/> with the same grouping and boundary handling, without building the
    /// sortable keys. Meant for the hot evaluation path.
    /// </summary>
    /// <param name="scenario">Plan to measure</param>
    /// <param name="context">Wizard context supplying the works outside the genome</param>
    public static (int ShortCount, int TotalCount) Counts(CoreScenario scenario, CoreWizardContext context)
    {
        var shortCount = 0;
        var totalCount = 0;
        foreach (var (_, dates) in DatesByAgent(scenario, context))
        {
            foreach (var run in Runs(dates))
            {
                totalCount++;
                if (run.End.DayNumber - run.Start.DayNumber + 1 <= ShortPackageMaxDays)
                {
                    shortCount++;
                }
            }
        }

        return (shortCount, totalCount);
    }

    private static Dictionary<string, SortedSet<DateOnly>> DatesByAgent(
        CoreScenario scenario, CoreWizardContext context)
    {
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

        return datesByAgent;
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
