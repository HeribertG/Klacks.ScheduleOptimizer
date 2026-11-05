// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Constraints.Keywords;

/// <summary>
/// Wizard 1 view of the keyword wishes: the keyword days of a <see cref="CoreWizardContext"/>, built once per context
/// and cached, and the evaluation of a token plan through <see cref="KeywordWishCalculator"/>. A positive wish on a day
/// the employee cannot work (absence, no contract day) is dropped, because no plan could fulfil it.
/// </summary>
/// <param name="context">The run's wizard context.</param>
public sealed class WizardKeywordWishes
{
    private static readonly ConditionalWeakTable<CoreWizardContext, WizardKeywordWishes> Cache = new();

    private WizardKeywordWishes(CoreWizardContext context)
    {
        var days = KeywordWishCalculator.BuildDays(
            context.ScheduleCommands.Select(c => (c.AgentId, c.Date, c.Keyword)));
        var agents = context.Agents.ToDictionary(a => a.Id, StringComparer.Ordinal);
        foreach (var key in days.Keys.ToList())
        {
            if (days[key].PositiveWish is not null && !CanWork(context, agents, key.AgentId, key.Date))
            {
                days[key] = days[key].WithoutPositiveWish();
            }
        }

        Days = days;
    }

    /// <summary>Keyword days per (agent, day).</summary>
    public IReadOnlyDictionary<(string AgentId, DateOnly Date), KeywordDay> Days { get; }

    public static WizardKeywordWishes For(CoreWizardContext context) => Cache.GetValue(context, static c => new WizardKeywordWishes(c));

    /// <summary>True when a shift of the kind on the day breaks the employee's keyword restriction.</summary>
    /// <param name="agentId">The employee.</param>
    /// <param name="date">Start day of the shift.</param>
    /// <param name="shiftTypeIndex">Kind of the shift (ShiftTypeInference).</param>
    public bool BreaksRestriction(string agentId, DateOnly date, int shiftTypeIndex)
        => Days.TryGetValue((agentId, date), out var day)
            && !day.Allows(KeywordShiftKinds.FromShiftTypeIndex(shiftTypeIndex));

    /// <summary>Missed wishes of a token plan; locked tokens count, they are part of the plan.</summary>
    /// <param name="tokens">All tokens of the plan.</param>
    /// <param name="collectItems">True to list the single missed wishes.</param>
    public KeywordWishResult Evaluate(IReadOnlyList<CoreToken> tokens, bool collectItems)
    {
        if (Days.Count == 0)
        {
            return KeywordWishResult.Empty;
        }

        var worked = new Dictionary<(string AgentId, DateOnly Date), int>();
        foreach (var token in tokens)
        {
            var key = (token.AgentId, token.Date);
            if (Days.ContainsKey(key))
            {
                worked[key] = worked.GetValueOrDefault(key) | KeywordShiftKinds.FromShiftTypeIndex(token.ShiftTypeIndex);
            }
        }

        return KeywordWishCalculator.Evaluate(Days, worked, collectItems);
    }

    private static bool CanWork(
        CoreWizardContext context, IReadOnlyDictionary<string, CoreAgent> agents, string agentId, DateOnly date)
    {
        foreach (var blocker in context.BreakBlockers)
        {
            if (blocker.AgentId == agentId && date >= blocker.FromInclusive && date <= blocker.UntilInclusive)
            {
                return false;
            }
        }

        var worksOnDate = context.WorksOnDate(agentId, date);
        if (worksOnDate.HasValue)
        {
            return worksOnDate.Value;
        }

        return !agents.TryGetValue(agentId, out var agent) || agent.WorksOnWeekday(date.DayOfWeek);
    }
}
