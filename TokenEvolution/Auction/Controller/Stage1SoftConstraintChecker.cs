// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Auction.Controller;

/// <summary>
/// Stage 1 — Soft. Rules that should normally hold but may be relaxed if no Stage-0-clean
/// alternative exists. Currently checks: MaxWorkDays (preferred block-length cap, e.g. "5 days
/// then rest"), MinRestDays (gap between blocks), PreferredShift (slot is in the agent's
/// Preferred list when any preference exists). MaxConsecutiveDays is a HARD cap and lives
/// in Stage0HardConstraintChecker.
/// </summary>
public sealed class Stage1SoftConstraintChecker
{
    public VetoVerdict? Check(
        CoreAgent agent,
        CoreShift slot,
        IReadOnlyList<CoreToken> alreadyAssigned,
        CoreWizardContext context)
    {
        if (!DateOnly.TryParse(slot.Date, out var date))
        {
            return null;
        }

        var blockVeto = CheckBlockLength(agent, date, alreadyAssigned);
        if (blockVeto != null)
        {
            return blockVeto;
        }

        var restVeto = CheckMinRestDays(agent, date, alreadyAssigned);
        if (restVeto != null)
        {
            return restVeto;
        }

        return CheckPreferredShift(agent, slot, date, context.ShiftPreferences);
    }

    private static VetoVerdict? CheckPreferredShift(
        CoreAgent agent, CoreShift slot, DateOnly date, IReadOnlyList<CoreShiftPreference> preferences)
    {
        if (preferences.Count == 0 || string.IsNullOrEmpty(slot.Id) || !Guid.TryParse(slot.Id, out var slotShiftRefId))
        {
            return null;
        }

        var hasAnyPreferred = false;
        var slotIsPreferred = false;
        foreach (var pref in preferences)
        {
            if (pref.AgentId != agent.Id || pref.Kind != ShiftPreferenceKind.Preferred)
            {
                continue;
            }
            hasAnyPreferred = true;
            if (pref.ShiftRefId == slotShiftRefId)
            {
                slotIsPreferred = true;
                break;
            }
        }

        if (hasAnyPreferred && !slotIsPreferred)
        {
            return new VetoVerdict(1, "PreferredShift",
                $"Agent {agent.Id} has explicit shift preferences but slot {slot.Id} on {date:yyyy-MM-dd} is not in the preferred list.");
        }
        return null;
    }

    private static VetoVerdict? CheckBlockLength(
        CoreAgent agent, DateOnly date, IReadOnlyList<CoreToken> assigned)
    {
        var softCap = agent.MaxWorkDays > 0 ? agent.MaxWorkDays : 0;
        if (softCap <= 0)
        {
            return null;
        }

        var before = CountConsecutive(agent.Id, date, assigned, step: -1);
        var after = CountConsecutive(agent.Id, date, assigned, step: +1);
        var runLength = before + 1 + after;

        if (runLength > softCap)
        {
            return new VetoVerdict(1, "MaxWorkDays",
                $"Agent {agent.Id} would create a {runLength}-day work block on {date:yyyy-MM-dd}; soft cap is {softCap}.");
        }
        return null;
    }

    private static VetoVerdict? CheckMinRestDays(
        CoreAgent agent, DateOnly date, IReadOnlyList<CoreToken> assigned)
    {
        if (agent.MinRestDays <= 0)
        {
            return null;
        }

        var hasPrev = HasAssignmentOnDate(agent.Id, date.AddDays(-1), assigned);
        var hasNext = HasAssignmentOnDate(agent.Id, date.AddDays(+1), assigned);

        if (!hasPrev)
        {
            var lastBefore = FindNearestOccupiedDate(agent.Id, date, assigned, step: -1);
            if (lastBefore.HasValue)
            {
                var gap = (date.DayNumber - lastBefore.Value.DayNumber) - 1;
                if (gap < agent.MinRestDays)
                {
                    return new VetoVerdict(1, "MinRestDays",
                        $"Agent {agent.Id} would have only {gap} rest day(s) before {date:yyyy-MM-dd}; required {agent.MinRestDays}.");
                }
            }
        }

        if (!hasNext)
        {
            var firstAfter = FindNearestOccupiedDate(agent.Id, date, assigned, step: +1);
            if (firstAfter.HasValue)
            {
                var gap = (firstAfter.Value.DayNumber - date.DayNumber) - 1;
                if (gap < agent.MinRestDays)
                {
                    return new VetoVerdict(1, "MinRestDays",
                        $"Agent {agent.Id} would have only {gap} rest day(s) after {date:yyyy-MM-dd}; required {agent.MinRestDays}.");
                }
            }
        }
        return null;
    }

    private static int CountConsecutive(
        string agentId, DateOnly anchor, IReadOnlyList<CoreToken> assigned, int step)
    {
        var count = 0;
        var probe = anchor.AddDays(step);
        while (HasAssignmentOnDate(agentId, probe, assigned))
        {
            count++;
            probe = probe.AddDays(step);
        }
        return count;
    }

    private static bool HasAssignmentOnDate(
        string agentId, DateOnly date, IReadOnlyList<CoreToken> assigned)
    {
        foreach (var t in assigned)
        {
            if (t.AgentId == agentId && t.Date == date)
            {
                return true;
            }
        }
        return false;
    }

    private static DateOnly? FindNearestAssignedDate(
        string agentId, DateOnly anchor, IReadOnlyList<CoreToken> assigned, int step)
    {
        DateOnly? best = null;
        foreach (var t in assigned)
        {
            if (t.AgentId != agentId)
            {
                continue;
            }
            if (step < 0 && t.Date < anchor)
            {
                if (!best.HasValue || t.Date > best.Value)
                {
                    best = t.Date;
                }
            }
            else if (step > 0 && t.Date > anchor)
            {
                if (!best.HasValue || t.Date < best.Value)
                {
                    best = t.Date;
                }
            }
        }
        return best;
    }

    private static DateOnly? FindNearestOccupiedDate(
        string agentId, DateOnly anchor, IReadOnlyList<CoreToken> assigned, int step)
    {
        DateOnly? best = null;
        foreach (var t in assigned)
        {
            if (t.AgentId != agentId) continue;
            ConsiderDate(t.Date, anchor, step, ref best);
            if (CrossesMidnight(t))
            {
                ConsiderDate(DateOnly.FromDateTime(t.EndAt), anchor, step, ref best);
            }
        }
        return best;
    }

    private static void ConsiderDate(DateOnly candidate, DateOnly anchor, int step, ref DateOnly? best)
    {
        if (step < 0 && candidate < anchor)
        {
            if (!best.HasValue || candidate > best.Value) best = candidate;
        }
        else if (step > 0 && candidate > anchor)
        {
            if (!best.HasValue || candidate < best.Value) best = candidate;
        }
    }

    private static bool CrossesMidnight(CoreToken t) =>
        t.EndAt.Date > t.StartAt.Date;
}
