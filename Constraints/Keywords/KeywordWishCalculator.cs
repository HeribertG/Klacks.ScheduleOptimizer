// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Constraints.Keywords;

/// <summary>
/// Single source of truth of the keyword wishes: judges a day's worked shift kinds against its <see cref="KeywordDay"/>
/// and sums a whole plan. Wizard 1 fitness, the harmonizer move guard and the final count after apply all use it, so the
/// three can never disagree on what a broken or unfulfilled wish is.
/// </summary>
public static class KeywordWishCalculator
{
    /// <summary>Verdict of one (agent, day).</summary>
    /// <param name="day">The day's keyword rules.</param>
    /// <param name="workedKinds">Kinds worked that day (<see cref="KeywordShiftKinds"/>), keyed on the shift's start day.</param>
    public static KeywordDayVerdict EvaluateDay(KeywordDay day, int workedKinds)
    {
        var broken = workedKinds != KeywordShiftKinds.None && !day.Allows(workedKinds)
            ? day.RestrictionOutcome
            : null;
        var wanted = day.WantedKinds;
        var unfulfilled = wanted != KeywordShiftKinds.None && (workedKinds & wanted) == KeywordShiftKinds.None;
        return new KeywordDayVerdict(broken, unfulfilled);
    }

    /// <summary>Verdict of one (agent, day); clean without a keyword day.</summary>
    /// <param name="day">The day's keyword rules, null when the day has none.</param>
    /// <param name="workedKinds">Kinds worked that day.</param>
    public static KeywordDayVerdict VerdictOf(KeywordDay? day, int workedKinds)
        => day is null ? KeywordDayVerdict.Clean : EvaluateDay(day, workedKinds);

    /// <summary>Sums the plan over every keyword day.</summary>
    /// <param name="days">Keyword days of the plan per (agent, day).</param>
    /// <param name="workedKinds">Worked kinds per (agent, day); a missing key is a day without work.</param>
    /// <param name="collectItems">True to list the single missed wishes as well.</param>
    public static KeywordWishResult Evaluate(
        IReadOnlyDictionary<(string AgentId, DateOnly Date), KeywordDay> days,
        IReadOnlyDictionary<(string AgentId, DateOnly Date), int> workedKinds,
        bool collectItems)
    {
        if (days.Count == 0)
        {
            return KeywordWishResult.Empty;
        }

        var tally = default(KeywordWishTally);
        var items = collectItems ? new List<KeywordWishItem>() : null;
        foreach (var (key, day) in days)
        {
            var verdict = EvaluateDay(day, workedKinds.GetValueOrDefault(key, KeywordShiftKinds.None));
            if (verdict.IsClean)
            {
                continue;
            }

            tally = tally.Add(verdict);
            if (items is null)
            {
                continue;
            }

            if (verdict.Broken is { } broken)
            {
                items.Add(new KeywordWishItem(key.AgentId, key.Date, day.Combined, broken));
            }

            if (verdict.Unfulfilled)
            {
                items.Add(new KeywordWishItem(
                    key.AgentId, key.Date, day.PositiveWish ?? ScheduleCommandKeyword.NotFree, KeywordWishOutcome.Unfulfilled));
            }
        }

        if (items is null)
        {
            return new KeywordWishResult(tally, []);
        }

        items.Sort(static (a, b) =>
        {
            var byAgent = string.CompareOrdinal(a.AgentId, b.AgentId);
            return byAgent != 0 ? byAgent : a.Date.CompareTo(b.Date);
        });
        return new KeywordWishResult(tally, items);
    }

    /// <summary>Groups keywords per (agent, day) into keyword days.</summary>
    /// <param name="commands">Recognized commands in any order.</param>
    public static Dictionary<(string AgentId, DateOnly Date), KeywordDay> BuildDays(
        IEnumerable<(string AgentId, DateOnly Date, ScheduleCommandKeyword Keyword)> commands)
    {
        var days = new Dictionary<(string AgentId, DateOnly Date), KeywordDay>();
        foreach (var group in commands.GroupBy(c => (c.AgentId, c.Date)))
        {
            if (KeywordDay.From(group.Select(c => c.Keyword)) is { } day)
            {
                days[group.Key] = day;
            }
        }

        return days;
    }
}
