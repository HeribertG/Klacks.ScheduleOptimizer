// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Auction.Controller;

/// <summary>
/// Stage 0 — Tabu. These rules can NEVER be broken: availability, contract weekday,
/// FREE keyword, MaxDailyHours (per-agent), MinPauseHours (per-agent), MaxConsecutiveDays
/// (per-agent hard cap on block length), double-booking (incl. cross-day overnight overlap),
/// existing-work overlap. If all candidates fail here for a slot, the slot stays unassigned
/// and the dispatcher must intervene.
/// </summary>
public sealed class Stage0HardConstraintChecker
{
    /// <summary>
    /// Validates an entire scenario by re-running the per-slot Stage 0 check for every
    /// non-locked token against the rest of the tokens. Used by GA operators (swap, crossover,
    /// merge) to refuse mutations that introduce new hard-constraint violations.
    /// </summary>
    public VetoVerdict? ValidateScenario(
        IReadOnlyList<CoreToken> tokens,
        CoreWizardContext context)
    {
        var agentLookup = new Dictionary<string, CoreAgent>(StringComparer.Ordinal);
        foreach (var a in context.Agents)
        {
            agentLookup[a.Id] = a;
        }

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.IsLocked)
            {
                continue;
            }

            if (!agentLookup.TryGetValue(token.AgentId, out var agent))
            {
                return new VetoVerdict(0, "UnknownAgent",
                    $"Token references unknown agent {token.AgentId}.");
            }

            var others = new List<CoreToken>(tokens.Count - 1);
            for (var j = 0; j < tokens.Count; j++)
            {
                if (j != i)
                {
                    others.Add(tokens[j]);
                }
            }

            var slot = TokenToSlot(token);
            var verdict = Check(agent, slot, others, context);
            if (verdict != null)
            {
                return verdict;
            }
        }

        return null;
    }

    /// <summary>
    /// Validates a single token against the rest of a scenario. Cheaper than
    /// <see cref="ValidateScenario"/> when only a small number of tokens were modified.
    /// </summary>
    public VetoVerdict? ValidateToken(
        CoreToken token,
        IReadOnlyList<CoreToken> otherTokens,
        CoreWizardContext context)
    {
        if (token.IsLocked)
        {
            return null;
        }

        CoreAgent? agent = null;
        foreach (var a in context.Agents)
        {
            if (a.Id == token.AgentId)
            {
                agent = a;
                break;
            }
        }

        if (agent is null)
        {
            return new VetoVerdict(0, "UnknownAgent",
                $"Token references unknown agent {token.AgentId}.");
        }

        var slot = TokenToSlot(token);
        return Check(agent, slot, otherTokens, context);
    }

    private static CoreShift TokenToSlot(CoreToken token)
    {
        var startTime = TimeOnly.FromDateTime(token.StartAt).ToString("HH:mm");
        var endTime = TimeOnly.FromDateTime(token.EndAt).ToString("HH:mm");
        var date = token.Date.ToString("yyyy-MM-dd");
        return new CoreShift(
            Id: token.ShiftRefId.ToString(),
            Name: string.Empty,
            Date: date,
            StartTime: startTime,
            EndTime: endTime,
            Hours: (double)token.TotalHours,
            RequiredAssignments: 1,
            Priority: 0);
    }

    public VetoVerdict? Check(
        CoreAgent agent,
        CoreShift slot,
        IReadOnlyList<CoreToken> alreadyAssigned,
        CoreWizardContext context)
    {
        var date = ParseDateOrNull(slot.Date);
        if (date is null)
        {
            return new VetoVerdict(0, "InvalidSlotDate", $"Slot date '{slot.Date}' is not parseable.");
        }

        var start = ParseTimeOrDefault(slot.StartTime, new TimeOnly(8, 0));
        var shiftTypeIndex = ShiftTypeInference.FromStartTime(start);
        var slotHours = (decimal)slot.Hours;

        if (ExceedsMaxConsecutiveDays(agent, date.Value, alreadyAssigned, context, out var runLength, out var hardCap))
        {
            return new VetoVerdict(0, "MaxConsecutiveDays",
                $"Agent {agent.Id} would create a {runLength}-day work block on {date.Value:yyyy-MM-dd}; hard cap is {hardCap}.");
        }

        if (!RespectsWeekday(agent, date.Value.DayOfWeek))
        {
            return new VetoVerdict(0, "ContractWeekday",
                $"Agent {agent.Id} is not contracted to work on {date.Value.DayOfWeek}.");
        }

        if (!agent.PerformsShiftWork && shiftTypeIndex != 0)
        {
            return new VetoVerdict(0, "PerformsShiftWork",
                $"Agent {agent.Id} does not participate in shift rotation; only early-shift permitted.");
        }

        if (IsBlockedByBreak(agent.Id, date.Value, context.BreakBlockers))
        {
            return new VetoVerdict(0, "BreakBlocker",
                $"Agent {agent.Id} is blocked by an absence on {date.Value:yyyy-MM-dd}.");
        }

        var keywordVeto = CheckKeyword(agent.Id, date.Value, shiftTypeIndex, context.ScheduleCommands);
        if (keywordVeto != null)
        {
            return keywordVeto;
        }

        if (IsBlacklistedShift(agent.Id, slot.Id, context.ShiftPreferences))
        {
            return new VetoVerdict(0, "BlacklistedShift",
                $"Agent {agent.Id} is blacklisted from shift {slot.Id} on {date.Value:yyyy-MM-dd}.");
        }

        if (agent.MaximumHours > 0 && ExceedsMaxHours(agent, slotHours, alreadyAssigned))
        {
            return new VetoVerdict(0, "MaximumHoursContractCap",
                $"Agent {agent.Id} would exceed contract MaximumHours of {agent.MaximumHours}.");
        }

        if (ExceedsDailyHours(agent, date.Value, slotHours, context, alreadyAssigned))
        {
            var cap = ResolveDailyCap(agent, date.Value, context);
            return new VetoVerdict(0, "MaxDailyHours",
                $"Agent {agent.Id} would exceed daily-hours cap of {cap}h on {date.Value:yyyy-MM-dd}.");
        }

        if (HasOverlappingShift(agent.Id, slot, date.Value, alreadyAssigned))
        {
            return new VetoVerdict(0, "OverlappingShift",
                $"Agent {agent.Id} already has a shift overlapping with {slot.StartTime}-{slot.EndTime} on {date.Value:yyyy-MM-dd}.");
        }

        if (HasOverlappingExistingWork(agent.Id, slot, date.Value, context.ExistingWorkBlockers))
        {
            return new VetoVerdict(0, "ExistingWorkOverlap",
                $"Agent {agent.Id} already has an unlocked Work in the database overlapping with {slot.StartTime}-{slot.EndTime} on {date.Value:yyyy-MM-dd}.");
        }

        if (ViolatesMinPauseHours(agent, slot, date.Value, alreadyAssigned, context))
        {
            return new VetoVerdict(0, "MinPauseHours",
                $"Agent {agent.Id} would have less than {agent.MinRestHours}h rest before/after {slot.StartTime}-{slot.EndTime} on {date.Value:yyyy-MM-dd}.");
        }

        return null;
    }

    private static bool RespectsWeekday(CoreAgent agent, DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => agent.WorkOnMonday,
        DayOfWeek.Tuesday => agent.WorkOnTuesday,
        DayOfWeek.Wednesday => agent.WorkOnWednesday,
        DayOfWeek.Thursday => agent.WorkOnThursday,
        DayOfWeek.Friday => agent.WorkOnFriday,
        DayOfWeek.Saturday => agent.WorkOnSaturday,
        DayOfWeek.Sunday => agent.WorkOnSunday,
        _ => false,
    };

    private static bool IsBlacklistedShift(string agentId, string slotShiftRefId, IReadOnlyList<CoreShiftPreference> preferences)
    {
        if (preferences.Count == 0 || string.IsNullOrEmpty(slotShiftRefId))
        {
            return false;
        }
        if (!Guid.TryParse(slotShiftRefId, out var shiftRefId))
        {
            return false;
        }
        foreach (var pref in preferences)
        {
            if (pref.AgentId == agentId && pref.ShiftRefId == shiftRefId && pref.Kind == ShiftPreferenceKind.Blacklist)
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsBlockedByBreak(string agentId, DateOnly date, IReadOnlyList<CoreBreakBlocker> blockers)
    {
        foreach (var blocker in blockers)
        {
            if (blocker.AgentId == agentId && date >= blocker.FromInclusive && date <= blocker.UntilInclusive)
            {
                return true;
            }
        }
        return false;
    }

    private static VetoVerdict? CheckKeyword(
        string agentId, DateOnly date, int shiftTypeIndex, IReadOnlyList<CoreScheduleCommand> commands)
    {
        foreach (var cmd in commands)
        {
            if (cmd.AgentId != agentId || cmd.Date != date)
            {
                continue;
            }

            var name = cmd.Keyword.ToString();
            switch (cmd.Keyword)
            {
                case ScheduleCommandKeyword.Free:
                    return new VetoVerdict(0, "KeywordFree",
                        $"Agent {agentId} marked FREE on {date:yyyy-MM-dd}.");
                case ScheduleCommandKeyword.OnlyEarly when shiftTypeIndex != 0:
                case ScheduleCommandKeyword.NoEarly when shiftTypeIndex == 0:
                case ScheduleCommandKeyword.OnlyLate when shiftTypeIndex != 1:
                case ScheduleCommandKeyword.NoLate when shiftTypeIndex == 1:
                case ScheduleCommandKeyword.OnlyNight when shiftTypeIndex != 2:
                case ScheduleCommandKeyword.NoNight when shiftTypeIndex == 2:
                    return new VetoVerdict(0, $"Keyword{name}",
                        $"Slot type conflicts with keyword '{name}' on {date:yyyy-MM-dd}.");
            }
        }
        return null;
    }

    private static bool ExceedsMaxHours(CoreAgent agent, decimal slotHours, IReadOnlyList<CoreToken> assigned)
    {
        decimal sumAssigned = 0;
        foreach (var t in assigned)
        {
            if (t.AgentId == agent.Id)
            {
                sumAssigned += t.TotalHours;
            }
        }
        return (double)(sumAssigned + slotHours) + agent.CurrentHours > agent.MaximumHours;
    }

    private static bool ExceedsDailyHours(
        CoreAgent agent,
        DateOnly date,
        decimal slotHours,
        CoreWizardContext context,
        IReadOnlyList<CoreToken> assigned)
    {
        var cap = ResolveDailyCap(agent, date, context);
        if (cap <= 0)
        {
            return false;
        }

        decimal sumDay = 0;
        foreach (var t in assigned)
        {
            if (t.AgentId == agent.Id && t.Date == date)
            {
                sumDay += t.TotalHours;
            }
        }
        return (double)(sumDay + slotHours) > cap;
    }

    private static double ResolveDailyCap(CoreAgent agent, DateOnly date, CoreWizardContext context)
    {
        foreach (var day in context.ContractDays)
        {
            if (day.AgentId == agent.Id && day.Date == date && day.MaximumHoursPerDay > 0)
            {
                return day.MaximumHoursPerDay;
            }
        }

        return agent.MaxDailyHours > 0 ? agent.MaxDailyHours : context.SchedulingMaxDailyHours;
    }

    private static bool ExceedsMaxConsecutiveDays(
        CoreAgent agent,
        DateOnly date,
        IReadOnlyList<CoreToken> assigned,
        CoreWizardContext context,
        out int runLength,
        out int hardCap)
    {
        hardCap = agent.MaxConsecutiveDays > 0
            ? agent.MaxConsecutiveDays
            : context.SchedulingMaxConsecutiveDays;
        runLength = 0;

        if (hardCap <= 0)
        {
            return false;
        }

        var before = CountConsecutive(agent.Id, date, assigned, context, step: -1);
        var after = CountConsecutive(agent.Id, date, assigned, context, step: +1);
        runLength = before + 1 + after;

        return runLength > hardCap;
    }

    private static int CountConsecutive(
        string agentId, DateOnly anchor, IReadOnlyList<CoreToken> assigned, CoreWizardContext context, int step)
    {
        var count = 0;
        var probe = anchor.AddDays(step);
        while (HasWorkOnDate(agentId, probe, assigned, context))
        {
            count++;
            probe = probe.AddDays(step);
        }
        return count;
    }

    /// <summary>
    /// Checks whether the agent has any work on a given date — including boundary works outside
    /// [PeriodFrom, PeriodUntil]. A break on the date is NOT a work and the consecutive walk stops there
    /// (matches the in-period semantics where a free or break day breaks the streak).
    /// </summary>
    private static bool HasWorkOnDate(
        string agentId, DateOnly date, IReadOnlyList<CoreToken> assigned, CoreWizardContext context)
    {
        foreach (var t in assigned)
        {
            if (t.AgentId == agentId && t.Date == date)
            {
                return true;
            }
        }

        // Boundary context: works on days adjacent to the period count toward MaxConsecutiveDays runs
        // crossing the period start or end, but are never planned by the GA.
        if (date < context.PeriodFrom || date > context.PeriodUntil)
        {
            foreach (var locked in context.BoundaryLockedWorks)
            {
                if (locked.AgentId == agentId && locked.Date == date)
                {
                    return true;
                }
            }
            foreach (var blocker in context.BoundaryExistingWorkBlockers)
            {
                if (blocker.AgentId == agentId && blocker.Date == date)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasOverlappingShift(
        string agentId, CoreShift slot, DateOnly date, IReadOnlyList<CoreToken> assigned)
    {
        if (!TryGetSlotInterval(slot, date, out var slotStartUtc, out var slotEndUtc))
        {
            return false;
        }

        foreach (var t in assigned)
        {
            if (t.AgentId != agentId)
            {
                continue;
            }
            if (t.StartAt < slotEndUtc && slotStartUtc < t.EndAt)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasOverlappingExistingWork(
        string agentId, CoreShift slot, DateOnly date, IReadOnlyList<CoreExistingWorkBlocker> blockers)
    {
        if (blockers.Count == 0 || !TryGetSlotInterval(slot, date, out var slotStartUtc, out var slotEndUtc))
        {
            return false;
        }

        foreach (var b in blockers)
        {
            if (b.AgentId != agentId)
            {
                continue;
            }
            if (b.StartAt < slotEndUtc && slotStartUtc < b.EndAt)
            {
                return true;
            }
        }
        return false;
    }

    private static bool ViolatesMinPauseHours(
        CoreAgent agent,
        CoreShift slot,
        DateOnly date,
        IReadOnlyList<CoreToken> assigned,
        CoreWizardContext context)
    {
        var minRest = agent.MinRestHours > 0 ? agent.MinRestHours : context.SchedulingMinPauseHours;
        if (minRest <= 0)
        {
            return false;
        }

        if (!TryGetSlotInterval(slot, date, out var slotStartUtc, out var slotEndUtc))
        {
            return false;
        }

        foreach (var t in assigned)
        {
            if (t.AgentId != agent.Id)
            {
                continue;
            }
            if (GapHoursBelow(slotStartUtc, slotEndUtc, t.StartAt, t.EndAt, minRest))
            {
                return true;
            }
        }

        foreach (var locked in context.LockedWorks)
        {
            if (locked.AgentId != agent.Id)
            {
                continue;
            }
            if (GapHoursBelow(slotStartUtc, slotEndUtc, locked.StartAt, locked.EndAt, minRest))
            {
                return true;
            }
        }

        foreach (var blocker in context.ExistingWorkBlockers)
        {
            if (blocker.AgentId != agent.Id)
            {
                continue;
            }
            if (GapHoursBelow(slotStartUtc, slotEndUtc, blocker.StartAt, blocker.EndAt, minRest))
            {
                return true;
            }
        }

        // Boundary context: locked / existing works on days adjacent to the period also constrain
        // the rest gap at the period start (Apr 30 late shift → May 1 early shift) and at the period end.
        foreach (var locked in context.BoundaryLockedWorks)
        {
            if (locked.AgentId != agent.Id)
            {
                continue;
            }
            if (GapHoursBelow(slotStartUtc, slotEndUtc, locked.StartAt, locked.EndAt, minRest))
            {
                return true;
            }
        }

        foreach (var blocker in context.BoundaryExistingWorkBlockers)
        {
            if (blocker.AgentId != agent.Id)
            {
                continue;
            }
            if (GapHoursBelow(slotStartUtc, slotEndUtc, blocker.StartAt, blocker.EndAt, minRest))
            {
                return true;
            }
        }

        return false;
    }

    private static bool GapHoursBelow(
        DateTime slotStart, DateTime slotEnd,
        DateTime otherStart, DateTime otherEnd,
        double minRestHours)
    {
        if (slotStart < otherEnd && otherStart < slotEnd)
        {
            return false;
        }

        var gapHours = slotStart >= otherEnd
            ? (slotStart - otherEnd).TotalHours
            : (otherStart - slotEnd).TotalHours;

        return gapHours >= 0 && gapHours < minRestHours;
    }

    private static bool TryGetSlotInterval(CoreShift slot, DateOnly date, out DateTime startUtc, out DateTime endUtc)
    {
        if (!TimeOnly.TryParse(slot.StartTime, out var slotStart) ||
            !TimeOnly.TryParse(slot.EndTime, out var slotEnd))
        {
            startUtc = default;
            endUtc = default;
            return false;
        }

        startUtc = date.ToDateTime(slotStart);
        endUtc = slotEnd <= slotStart ? date.AddDays(1).ToDateTime(slotEnd) : date.ToDateTime(slotEnd);
        return true;
    }

    private static DateOnly? ParseDateOrNull(string date) =>
        DateOnly.TryParse(date, out var parsed) ? parsed : null;

    private static TimeOnly ParseTimeOrDefault(string time, TimeOnly fallback) =>
        TimeOnly.TryParse(time, out var parsed) ? parsed : fallback;
}
