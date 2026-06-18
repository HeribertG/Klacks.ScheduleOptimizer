// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Constraints;

/// <summary>
/// Engine-neutral plan-level hard-constraint checker. Operates on a flat list of
/// <see cref="AssignmentView"/> plus the <see cref="CoreWizardContext"/>, so the identical rule set
/// runs against a Wizard-1 token genome (via TokenConstraintChecker) and a Harmonizer bitmap
/// (via the objective adapter). The detection logic is lifted verbatim from the former
/// TokenConstraintChecker; violation ordering is preserved so Wizard-1 fitness is unchanged.
/// </summary>
/// <param name="assignments">All non-free assignments of the plan being scored</param>
/// <param name="context">Aggregated scheduling context (agents, contracts, commands, shifts, eligibility)</param>
public sealed class PlanConstraintChecker
{
    public IReadOnlyList<ConstraintViolation> Check(IReadOnlyList<AssignmentView> assignments, CoreWizardContext context)
    {
        var violations = new List<ConstraintViolation>();

        CheckWorkOnDay(assignments, context, violations);
        CheckPerformsShiftWork(assignments, context, violations);
        CheckPerDayKeyword(assignments, context, violations);
        CheckBreakBlocker(assignments, context, violations);
        CheckMaxConsecutiveDays(assignments, context, violations);
        CheckMinPauseHours(assignments, context, violations);
        CheckMaxDailyHours(assignments, context, violations);
        CheckMaximumHoursExceeded(assignments, context, violations);
        CheckQualificationMismatch(assignments, context, violations);
        CheckSlotSupply(assignments, context, violations);

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

    private static void CheckWorkOnDay(IReadOnlyList<AssignmentView> assignments, CoreWizardContext context, List<ConstraintViolation> violations)
    {
        var contractLookup = context.ContractDays
            .ToLookup(d => (d.AgentId, d.Date));

        foreach (var a in assignments)
        {
            var match = contractLookup[(a.AgentId, a.Date)].FirstOrDefault();
            if (match is not null && !match.WorksOnDay)
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

    private static void CheckPerformsShiftWork(IReadOnlyList<AssignmentView> assignments, CoreWizardContext context, List<ConstraintViolation> violations)
    {
        var agentLookup = context.Agents.ToDictionary(a => a.Id);

        foreach (var a in assignments)
        {
            if (agentLookup.TryGetValue(a.AgentId, out var agent)
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

    private static void CheckPerDayKeyword(IReadOnlyList<AssignmentView> assignments, CoreWizardContext context, List<ConstraintViolation> violations)
    {
        var commandLookup = context.ScheduleCommands
            .ToLookup(c => (c.AgentId, c.Date));

        foreach (var a in assignments)
        {
            foreach (var cmd in commandLookup[(a.AgentId, a.Date)])
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

    private static void CheckBreakBlocker(IReadOnlyList<AssignmentView> assignments, CoreWizardContext context, List<ConstraintViolation> violations)
    {
        foreach (var a in assignments)
        {
            foreach (var blocker in context.BreakBlockers)
            {
                if (blocker.AgentId == a.AgentId
                    && a.Date >= blocker.FromInclusive
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

    private static void CheckMaxConsecutiveDays(IReadOnlyList<AssignmentView> assignments, CoreWizardContext context, List<ConstraintViolation> violations)
    {
        var agentLookup = context.Agents.ToDictionary(a => a.Id);
        var fallback = context.SchedulingMaxConsecutiveDays;

        foreach (var perAgent in assignments.GroupBy(t => t.AgentId))
        {
            var cap = agentLookup.TryGetValue(perAgent.Key, out var agent) && agent.MaxConsecutiveDays > 0
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

    private static void CheckMinPauseHours(IReadOnlyList<AssignmentView> assignments, CoreWizardContext context, List<ConstraintViolation> violations)
    {
        var agentLookup = context.Agents.ToDictionary(a => a.Id);
        var fallback = context.SchedulingMinPauseHours;

        foreach (var perAgent in assignments.GroupBy(t => t.AgentId))
        {
            var minPause = agentLookup.TryGetValue(perAgent.Key, out var agent) && agent.MinRestHours > 0
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
        }
    }

    private static void CheckMaxDailyHours(IReadOnlyList<AssignmentView> assignments, CoreWizardContext context, List<ConstraintViolation> violations)
    {
        var dayLookup = context.ContractDays
            .ToDictionary(d => (d.AgentId, d.Date));
        var agentLookup = context.Agents.ToDictionary(a => a.Id);

        foreach (var grouped in assignments.GroupBy(t => (t.AgentId, t.Date)))
        {
            var sumHours = (double)grouped.Sum(t => t.TotalHours);
            var cap = ResolveDailyCap(grouped.Key.AgentId, grouped.Key.Date, dayLookup, agentLookup, context);

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
        Dictionary<(string AgentId, DateOnly Date), CoreContractDay> dayLookup,
        Dictionary<string, CoreAgent> agentLookup,
        CoreWizardContext context)
    {
        if (dayLookup.TryGetValue((agentId, date), out var day) && day.MaximumHoursPerDay > 0)
        {
            return day.MaximumHoursPerDay;
        }

        if (agentLookup.TryGetValue(agentId, out var agent) && agent.MaxDailyHours > 0)
        {
            return agent.MaxDailyHours;
        }

        return context.SchedulingMaxDailyHours;
    }

    private static void CheckMaximumHoursExceeded(IReadOnlyList<AssignmentView> assignments, CoreWizardContext context, List<ConstraintViolation> violations)
    {
        foreach (var agent in context.Agents)
        {
            if (agent.MaximumHours <= 0)
            {
                continue;
            }

            var assigned = assignments
                .Where(t => t.AgentId == agent.Id)
                .Sum(t => (double)t.TotalHours);

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

    private static void CheckSlotSupply(IReadOnlyList<AssignmentView> assignments, CoreWizardContext context, List<ConstraintViolation> violations)
    {
        if (context.Shifts.Count == 0)
        {
            return;
        }

        var capacityPerSlot = new Dictionary<(Guid ShiftRefId, DateOnly Date), int>();
        foreach (var shift in context.Shifts)
        {
            if (!Guid.TryParse(shift.Id, out var shiftRefId))
            {
                continue;
            }

            if (!DateOnly.TryParse(shift.Date, out var date))
            {
                continue;
            }

            var key = (shiftRefId, date);
            capacityPerSlot.TryGetValue(key, out var current);
            capacityPerSlot[key] = current + Math.Max(1, shift.RequiredAssignments);
        }

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
