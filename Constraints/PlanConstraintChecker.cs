// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Constraints;

/// <summary>
/// Engine-neutral plan-level hard-constraint checker. Operates on a flat list of
/// <see cref="AssignmentView"/> plus the <see cref="CoreWizardContext"/>, so the identical rule set
/// runs against a Wizard-1 token genome (via TokenConstraintChecker) and a Harmonizer bitmap
/// (via the objective adapter). The detection logic is lifted verbatim from the former
/// TokenConstraintChecker; violation ordering is preserved so Wizard-1 fitness is unchanged.
/// Works that already exist in the database are counted as well: a planned assignment colliding with
/// one of them books the agent twice in reality, even though the plan alone looks conflict-free. In the
/// bitmap path those existing works ARE the cells being scored, so a naive comparison would report every
/// cell against itself; the self-match exclusion recognises the bitmap origin by the missing block id
/// plus containment of the blocker in the assignment span (the bitmap merges same-day works of one agent
/// into one widened cell, which is why containment and not equality is the criterion).
/// </summary>
/// <param name="assignments">All non-free assignments of the plan being scored</param>
/// <param name="context">Aggregated scheduling context (agents, contracts, commands, shifts, eligibility)</param>
public sealed class PlanConstraintChecker
{
    public IReadOnlyList<ConstraintViolation> Check(IReadOnlyList<AssignmentView> assignments, CoreWizardContext context)
    {
        var violations = new List<ConstraintViolation>();
        var eval = EvaluationContext.For(context);

        CheckWorkOnDay(assignments, eval, violations);
        CheckPerformsShiftWork(assignments, eval, violations);
        CheckPerDayKeyword(assignments, eval, violations);
        CheckBreakBlocker(assignments, eval, violations);
        CheckMaxConsecutiveDays(assignments, context, eval, violations);
        var externalIntervals = BuildExternalIntervalsByAgent(assignments, context);
        CheckMinPauseHours(assignments, context, eval, externalIntervals, violations);
        CheckOverlap(assignments, violations);
        CheckExistingWorkOverlap(assignments, externalIntervals, violations);
        CheckMaxDailyHours(assignments, context, eval, violations);
        CheckMaximumHoursExceeded(assignments, eval, violations);
        CheckQualificationMismatch(assignments, context, violations);
        CheckSlotSupply(assignments, context, eval, violations);

        return violations;
    }

    private static void CheckQualificationMismatch(IReadOnlyList<AssignmentView> assignments, CoreWizardContext context, List<ConstraintViolation> violations)
    {
        if (context.IneligibleAssignments.Count == 0)
        {
            return;
        }

        foreach (var a in assignments)
        {
            // Locked assignments are deliberate, immutable (e.g. an offline-certified override) — never flag them.
            // Empty shift refs (breaks/free) carry no requirement.
            if (a.IsLocked || a.ShiftRefId == Guid.Empty)
            {
                continue;
            }

            if (!context.IsEligible(a.AgentId, a.ShiftRefId, a.Date))
            {
                violations.Add(new ConstraintViolation(
                    Kind: ViolationKind.QualificationMissing,
                    AgentId: a.AgentId,
                    Date: a.Date,
                    TokenBlockId: a.BlockId,
                    Description: $"Agent {a.AgentId} lacks a mandatory qualification for shift {a.ShiftRefId} on {a.Date:yyyy-MM-dd}.",
                    ShiftRefId: a.ShiftRefId));
            }
        }
    }

    private static void CheckWorkOnDay(IReadOnlyList<AssignmentView> assignments, EvaluationContext eval, List<ConstraintViolation> violations)
    {
        foreach (var a in assignments)
        {
            if (eval.ContractDayByAgentDate.TryGetValue((a.AgentId, a.Date), out var match) && !match.WorksOnDay)
            {
                violations.Add(new ConstraintViolation(
                    ViolationKind.WorkOnDayViolation,
                    a.AgentId,
                    a.Date,
                    a.BlockId,
                    $"Agent {a.AgentId} does not work on {a.Date:yyyy-MM-dd} per contract."));
            }
        }
    }

    private static void CheckPerformsShiftWork(IReadOnlyList<AssignmentView> assignments, EvaluationContext eval, List<ConstraintViolation> violations)
    {
        foreach (var a in assignments)
        {
            if (eval.AgentsById.TryGetValue(a.AgentId, out var agent)
                && !agent.PerformsShiftWork
                && a.ShiftTypeIndex != 0)
            {
                violations.Add(new ConstraintViolation(
                    ViolationKind.PerformsShiftWorkViolation,
                    a.AgentId,
                    a.Date,
                    a.BlockId,
                    $"Agent {a.AgentId} does not perform shift work; shift-type {a.ShiftTypeIndex} not allowed."));
            }
        }
    }

    private static void CheckPerDayKeyword(IReadOnlyList<AssignmentView> assignments, EvaluationContext eval, List<ConstraintViolation> violations)
    {
        foreach (var a in assignments)
        {
            if (!eval.CommandsByAgentDate.TryGetValue((a.AgentId, a.Date), out var commands))
            {
                continue;
            }

            foreach (var cmd in commands)
            {
                if (ViolatesKeyword(cmd.Keyword, a.ShiftTypeIndex))
                {
                    violations.Add(new ConstraintViolation(
                        ViolationKind.PerDayKeywordViolation,
                        a.AgentId,
                        a.Date,
                        a.BlockId,
                        $"Token violates {cmd.Keyword} keyword."));
                }
            }
        }
    }

    private static bool ViolatesKeyword(ScheduleCommandKeyword keyword, int shiftTypeIndex) => keyword switch
    {
        ScheduleCommandKeyword.Free => true,
        ScheduleCommandKeyword.OnlyEarly => shiftTypeIndex != 0,
        ScheduleCommandKeyword.NoEarly => shiftTypeIndex == 0,
        ScheduleCommandKeyword.OnlyLate => shiftTypeIndex != 1,
        ScheduleCommandKeyword.NoLate => shiftTypeIndex == 1,
        ScheduleCommandKeyword.OnlyNight => shiftTypeIndex != 2,
        ScheduleCommandKeyword.NoNight => shiftTypeIndex == 2,
        _ => false,
    };

    private static void CheckBreakBlocker(IReadOnlyList<AssignmentView> assignments, EvaluationContext eval, List<ConstraintViolation> violations)
    {
        foreach (var a in assignments)
        {
            if (!eval.BreakBlockersByAgent.TryGetValue(a.AgentId, out var blockers))
            {
                continue;
            }

            foreach (var blocker in blockers)
            {
                if (a.Date >= blocker.FromInclusive
                    && a.Date <= blocker.UntilInclusive)
                {
                    violations.Add(new ConstraintViolation(
                        ViolationKind.BreakBlockerViolation,
                        a.AgentId,
                        a.Date,
                        a.BlockId,
                        $"Token collides with break blocker '{blocker.Reason}'."));
                }
            }
        }
    }

    private static void CheckMaxConsecutiveDays(IReadOnlyList<AssignmentView> assignments, CoreWizardContext context, EvaluationContext eval, List<ConstraintViolation> violations)
    {
        var fallback = context.SchedulingMaxConsecutiveDays;

        foreach (var perAgent in assignments.GroupBy(t => t.AgentId))
        {
            var cap = eval.AgentsById.TryGetValue(perAgent.Key, out var agent) && agent.MaxConsecutiveDays > 0
                ? agent.MaxConsecutiveDays
                : fallback;
            if (cap <= 0)
            {
                continue;
            }

            var workingDays = perAgent.Select(t => t.Date).Distinct().OrderBy(d => d).ToList();
            if (workingDays.Count == 0)
            {
                continue;
            }

            var runStart = workingDays[0];
            var runLength = 1;
            for (var i = 1; i < workingDays.Count; i++)
            {
                if (workingDays[i].DayNumber == workingDays[i - 1].DayNumber + 1)
                {
                    runLength++;
                }
                else
                {
                    if (runLength > cap)
                    {
                        violations.Add(new ConstraintViolation(
                            ViolationKind.MaxConsecutiveDays,
                            perAgent.Key,
                            runStart,
                            null,
                            $"Agent has {runLength} consecutive days starting {runStart:yyyy-MM-dd}, cap is {cap}."));
                    }
                    runStart = workingDays[i];
                    runLength = 1;
                }
            }

            if (runLength > cap)
            {
                violations.Add(new ConstraintViolation(
                    ViolationKind.MaxConsecutiveDays,
                    perAgent.Key,
                    runStart,
                    null,
                    $"Agent has {runLength} consecutive days starting {runStart:yyyy-MM-dd}, cap is {cap}."));
            }
        }
    }

    private static void CheckMinPauseHours(
        IReadOnlyList<AssignmentView> assignments,
        CoreWizardContext context,
        EvaluationContext eval,
        IReadOnlyDictionary<string, List<ExternalInterval>> externalIntervals,
        List<ConstraintViolation> violations)
    {
        var fallback = context.SchedulingMinPauseHours;

        foreach (var perAgent in assignments.GroupBy(t => t.AgentId))
        {
            var minPause = eval.AgentsById.TryGetValue(perAgent.Key, out var agent) && agent.MinRestHours > 0
                ? agent.MinRestHours
                : fallback;
            if (minPause <= 0)
            {
                continue;
            }

            var ordered = perAgent.OrderBy(t => t.StartAt).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                var gapHours = (ordered[i].StartAt - ordered[i - 1].EndAt).TotalHours;
                if (gapHours >= 0 && gapHours < minPause)
                {
                    violations.Add(new ConstraintViolation(
                        ViolationKind.MinPauseHours,
                        perAgent.Key,
                        ordered[i].Date,
                        ordered[i].BlockId,
                        $"Gap between tokens is {gapHours:F1}h, min is {minPause}h."));
                }
            }

            if (!externalIntervals.TryGetValue(perAgent.Key, out var external))
            {
                continue;
            }

            foreach (var a in ordered)
            {
                if (a.IsLocked)
                {
                    continue;
                }

                foreach (var interval in external)
                {
                    if (!GapHoursBelow(a.StartAt, a.EndAt, interval.StartAt, interval.EndAt, minPause, out var gap))
                    {
                        continue;
                    }

                    violations.Add(new ConstraintViolation(
                        ViolationKind.MinPauseHours,
                        perAgent.Key,
                        a.Date,
                        a.BlockId,
                        $"Gap to an existing work is {gap:F1}h, min is {minPause}h."));
                }
            }
        }
    }

    /// <summary>
    /// Collects the intervals of works that exist outside the plan and are therefore invisible to the
    /// pairwise checks: existing works inside the period, existing works on the boundary days and locked
    /// works on the boundary days. Locked works INSIDE the period are deliberately left out - they are
    /// already part of the plan (as locked tokens resp. locked cells) and covered by CheckOverlap.
    /// </summary>
    private static Dictionary<string, List<ExternalInterval>> BuildExternalIntervalsByAgent(
        IReadOnlyList<AssignmentView> assignments,
        CoreWizardContext context)
    {
        var result = new Dictionary<string, List<ExternalInterval>>(StringComparer.Ordinal);
        if (context.ExistingWorkBlockers.Count == 0
            && context.BoundaryExistingWorkBlockers.Count == 0
            && context.BoundaryLockedWorks.Count == 0)
        {
            return result;
        }

        foreach (var blocker in context.ExistingWorkBlockers)
        {
            if (IsOwnAssignment(assignments, blocker.AgentId, blocker.StartAt, blocker.EndAt))
            {
                continue;
            }

            Add(result, blocker.AgentId, blocker.StartAt, blocker.EndAt);
        }

        foreach (var blocker in context.BoundaryExistingWorkBlockers)
        {
            Add(result, blocker.AgentId, blocker.StartAt, blocker.EndAt);
        }

        foreach (var locked in context.BoundaryLockedWorks)
        {
            Add(result, locked.AgentId, locked.StartAt, locked.EndAt);
        }

        return result;

        static void Add(Dictionary<string, List<ExternalInterval>> target, string agentId, DateTime startAt, DateTime endAt)
        {
            if (startAt == default || endAt == default)
            {
                return;
            }

            if (!target.TryGetValue(agentId, out var list))
            {
                list = [];
                target[agentId] = list;
            }

            list.Add(new ExternalInterval(startAt, endAt));
        }
    }

    /// <summary>
    /// True when the blocker IS the assignment being scored rather than a separate work. Only the bitmap
    /// adapter produces assignments without a block id, and the bitmap widens same-day works of one agent
    /// into a single cell, so the blocker must be contained in the assignment span - not equal to it.
    /// </summary>
    private static bool IsOwnAssignment(
        IReadOnlyList<AssignmentView> assignments, string agentId, DateTime startAt, DateTime endAt)
    {
        foreach (var a in assignments)
        {
            if (a.BlockId is null
                && a.AgentId == agentId
                && a.StartAt <= startAt
                && endAt <= a.EndAt)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Flags a planned assignment that physically collides with a work already stored in the database.
    /// CheckOverlap only sees the plan itself, so without this an agent could be booked onto a shift that
    /// runs while an untouched existing work of the same agent is still going.
    /// </summary>
    private static void CheckExistingWorkOverlap(
        IReadOnlyList<AssignmentView> assignments,
        IReadOnlyDictionary<string, List<ExternalInterval>> externalIntervals,
        List<ConstraintViolation> violations)
    {
        if (externalIntervals.Count == 0)
        {
            return;
        }

        foreach (var a in assignments)
        {
            if (a.IsLocked || a.StartAt == default || a.EndAt == default)
            {
                continue;
            }

            if (!externalIntervals.TryGetValue(a.AgentId, out var external))
            {
                continue;
            }

            foreach (var interval in external)
            {
                if (interval.StartAt < a.EndAt && a.StartAt < interval.EndAt)
                {
                    violations.Add(new ConstraintViolation(
                        ViolationKind.ExistingWorkOverlap,
                        a.AgentId,
                        a.Date,
                        a.BlockId,
                        $"Agent {a.AgentId} assignment starting {a.StartAt:yyyy-MM-dd HH:mm} overlaps an existing work ending {interval.EndAt:yyyy-MM-dd HH:mm}."));
                }
            }
        }
    }

    /// <summary>
    /// Rest-gap semantics of Stage0HardConstraintChecker: a real overlap is not a rest violation (the
    /// overlap check reports it), otherwise the distance in either direction must reach the minimum.
    /// </summary>
    private static bool GapHoursBelow(
        DateTime start, DateTime end, DateTime otherStart, DateTime otherEnd, double minPause, out double gapHours)
    {
        gapHours = 0;
        if (start == default || end == default)
        {
            return false;
        }

        if (start < otherEnd && otherStart < end)
        {
            return false;
        }

        gapHours = start >= otherEnd
            ? (start - otherEnd).TotalHours
            : (otherStart - end).TotalHours;

        return gapHours >= 0 && gapHours < minPause;
    }

    private readonly record struct ExternalInterval(DateTime StartAt, DateTime EndAt);

    /// <summary>
    /// Flags a same-agent shift OVERLAP (one human in two places). The MinPause check above only fires on
    /// a non-negative gap that is too short; a true overlap produces a negative gap it deliberately skips,
    /// and there is no other temporal-collision rule — so without this check a force-assigned double-booking
    /// scores zero hard violations. Per agent the assignments are swept in start order against the running
    /// maximum end time, which also catches multi-shift and cross-midnight overlaps.
    /// </summary>
    private static void CheckOverlap(IReadOnlyList<AssignmentView> assignments, List<ConstraintViolation> violations)
    {
        foreach (var perAgent in assignments.GroupBy(t => t.AgentId))
        {
            var ordered = perAgent
                .Where(a => a.StartAt != default && a.EndAt != default)
                .OrderBy(a => a.StartAt)
                .ToList();
            if (ordered.Count < 2)
            {
                continue;
            }

            var maxEnd = ordered[0].EndAt;
            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].StartAt < maxEnd)
                {
                    violations.Add(new ConstraintViolation(
                        ViolationKind.Overlap,
                        perAgent.Key,
                        ordered[i].Date,
                        ordered[i].BlockId,
                        $"Agent {perAgent.Key} is double-booked: a shift starting {ordered[i].StartAt:yyyy-MM-dd HH:mm} overlaps an earlier shift ending {maxEnd:yyyy-MM-dd HH:mm}."));
                }

                if (ordered[i].EndAt > maxEnd)
                {
                    maxEnd = ordered[i].EndAt;
                }
            }
        }
    }

    private static void CheckMaxDailyHours(IReadOnlyList<AssignmentView> assignments, CoreWizardContext context, EvaluationContext eval, List<ConstraintViolation> violations)
    {
        foreach (var grouped in assignments.GroupBy(t => (t.AgentId, t.Date)))
        {
            var sumHours = (double)grouped.Sum(t => t.TotalHours);
            var cap = ResolveDailyCap(grouped.Key.AgentId, grouped.Key.Date, eval, context);

            if (cap > 0 && sumHours > cap)
            {
                violations.Add(new ConstraintViolation(
                    ViolationKind.MaxDailyHours,
                    grouped.Key.AgentId,
                    grouped.Key.Date,
                    null,
                    $"Daily hours {sumHours} exceed cap {cap}."));
            }
        }
    }

    private static double ResolveDailyCap(
        string agentId,
        DateOnly date,
        EvaluationContext eval,
        CoreWizardContext context)
    {
        if (eval.ContractDayByAgentDate.TryGetValue((agentId, date), out var day) && day.MaximumHoursPerDay > 0)
        {
            return day.MaximumHoursPerDay;
        }

        if (eval.AgentsById.TryGetValue(agentId, out var agent) && agent.MaxDailyHours > 0)
        {
            return agent.MaxDailyHours;
        }

        return context.SchedulingMaxDailyHours;
    }

    private static void CheckMaximumHoursExceeded(IReadOnlyList<AssignmentView> assignments, EvaluationContext eval, List<ConstraintViolation> violations)
    {
        if (eval.AgentsWithMaximumHours.Count == 0)
        {
            return;
        }

        var hoursByAgent = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var a in assignments)
        {
            hoursByAgent.TryGetValue(a.AgentId, out var sum);
            hoursByAgent[a.AgentId] = sum + (double)a.TotalHours;
        }

        foreach (var agent in eval.AgentsWithMaximumHours)
        {
            hoursByAgent.TryGetValue(agent.Id, out var assigned);

            if (agent.CurrentHours + assigned > agent.MaximumHours)
            {
                violations.Add(new ConstraintViolation(
                    ViolationKind.MaximumHoursExceeded,
                    agent.Id,
                    null,
                    null,
                    $"Total hours {agent.CurrentHours + assigned} exceed agent maximum {agent.MaximumHours}."));
            }
        }
    }

    private static void CheckSlotSupply(IReadOnlyList<AssignmentView> assignments, CoreWizardContext context, EvaluationContext eval, List<ConstraintViolation> violations)
    {
        if (context.Shifts.Count == 0)
        {
            return;
        }

        var capacityPerSlot = eval.SlotCapacities;
        if (capacityPerSlot.Count == 0)
        {
            return;
        }

        var tokensPerSlot = new Dictionary<(Guid ShiftRefId, DateOnly Date), int>();
        foreach (var a in assignments)
        {
            if (a.ShiftRefId == Guid.Empty)
            {
                continue;
            }

            var key = (a.ShiftRefId, a.Date);
            tokensPerSlot.TryGetValue(key, out var current);
            tokensPerSlot[key] = current + 1;
        }

        foreach (var slot in capacityPerSlot)
        {
            tokensPerSlot.TryGetValue(slot.Key, out var assigned);
            var deficit = slot.Value - assigned;
            for (var i = 0; i < deficit; i++)
            {
                violations.Add(new ConstraintViolation(
                    Kind: ViolationKind.UnderSupply,
                    AgentId: string.Empty,
                    Date: slot.Key.Date,
                    TokenBlockId: null,
                    Description: $"Shift {slot.Key.ShiftRefId} on {slot.Key.Date:yyyy-MM-dd} is understaffed ({assigned}/{slot.Value}).",
                    ShiftRefId: slot.Key.ShiftRefId));
            }
        }

        foreach (var entry in tokensPerSlot)
        {
            capacityPerSlot.TryGetValue(entry.Key, out var capacity);
            if (capacity <= 0)
            {
                continue;
            }

            var surplus = entry.Value - capacity;
            for (var i = 0; i < surplus; i++)
            {
                violations.Add(new ConstraintViolation(
                    Kind: ViolationKind.OverSupply,
                    AgentId: string.Empty,
                    Date: entry.Key.Date,
                    TokenBlockId: null,
                    Description: $"Shift {entry.Key.ShiftRefId} on {entry.Key.Date:yyyy-MM-dd} is overstaffed ({entry.Value}/{capacity}).",
                    ShiftRefId: entry.Key.ShiftRefId));
            }
        }
    }
}
