// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Constraints;

/// <summary>
/// Detects hard-constraint violations in a CoreScenario genome.
/// Returns a flat list of <see cref="ConstraintViolation"/> entries. The count is the Stage-0 fitness value
/// (must be minimised to zero for any feasible solution).
/// </summary>
public sealed class TokenConstraintChecker
{
    public IReadOnlyList<ConstraintViolation> Check(CoreScenario scenario, CoreWizardContext context)
    {
        var violations = new List<ConstraintViolation>();

        CheckWorkOnDay(scenario, context, violations);
        CheckPerformsShiftWork(scenario, context, violations);
        CheckPerDayKeyword(scenario, context, violations);
        CheckBreakBlocker(scenario, context, violations);
        CheckMaxConsecutiveDays(scenario, context, violations);
        CheckMinPauseHours(scenario, context, violations);
        CheckMaxDailyHours(scenario, context, violations);
        CheckMaximumHoursExceeded(scenario, context, violations);
        CheckUnderSupply(scenario, context, violations);

        return violations;
    }

    public int CountViolations(CoreScenario scenario, CoreWizardContext context)
    {
        return Check(scenario, context).Count;
    }

    private static void CheckWorkOnDay(CoreScenario scenario, CoreWizardContext context, List<ConstraintViolation> violations)
    {
        var contractLookup = context.ContractDays
            .ToLookup(d => (d.AgentId, d.Date));

        foreach (var token in scenario.Tokens)
        {
            var match = contractLookup[(token.AgentId, token.Date)].FirstOrDefault();
            if (match is not null && !match.WorksOnDay)
            {
                violations.Add(new ConstraintViolation(
                    ViolationKind.WorkOnDayViolation,
                    token.AgentId,
                    token.Date,
                    token.BlockId,
                    $"Agent {token.AgentId} does not work on {token.Date:yyyy-MM-dd} per contract."));
            }
        }
    }

    private static void CheckPerformsShiftWork(CoreScenario scenario, CoreWizardContext context, List<ConstraintViolation> violations)
    {
        var agentLookup = context.Agents.ToDictionary(a => a.Id);

        foreach (var token in scenario.Tokens)
        {
            if (agentLookup.TryGetValue(token.AgentId, out var agent)
                && !agent.PerformsShiftWork
                && token.ShiftTypeIndex != 0)
            {
                violations.Add(new ConstraintViolation(
                    ViolationKind.PerformsShiftWorkViolation,
                    token.AgentId,
                    token.Date,
                    token.BlockId,
                    $"Agent {token.AgentId} does not perform shift work; shift-type {token.ShiftTypeIndex} not allowed."));
            }
        }
    }

    private static void CheckPerDayKeyword(CoreScenario scenario, CoreWizardContext context, List<ConstraintViolation> violations)
    {
        var commandLookup = context.ScheduleCommands
            .ToLookup(c => (c.AgentId, c.Date));

        foreach (var token in scenario.Tokens)
        {
            foreach (var cmd in commandLookup[(token.AgentId, token.Date)])
            {
                if (ViolatesKeyword(cmd.Keyword, token.ShiftTypeIndex))
                {
                    violations.Add(new ConstraintViolation(
                        ViolationKind.PerDayKeywordViolation,
                        token.AgentId,
                        token.Date,
                        token.BlockId,
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

    private static void CheckBreakBlocker(CoreScenario scenario, CoreWizardContext context, List<ConstraintViolation> violations)
    {
        foreach (var token in scenario.Tokens)
        {
            foreach (var blocker in context.BreakBlockers)
            {
                if (blocker.AgentId == token.AgentId
                    && token.Date >= blocker.FromInclusive
                    && token.Date <= blocker.UntilInclusive)
                {
                    violations.Add(new ConstraintViolation(
                        ViolationKind.BreakBlockerViolation,
                        token.AgentId,
                        token.Date,
                        token.BlockId,
                        $"Token collides with break blocker '{blocker.Reason}'."));
                }
            }
        }
    }

    private static void CheckMaxConsecutiveDays(CoreScenario scenario, CoreWizardContext context, List<ConstraintViolation> violations)
    {
        var cap = context.SchedulingMaxConsecutiveDays;
        if (cap <= 0)
        {
            return;
        }

        foreach (var block in scenario.Tokens.GroupBy(t => t.BlockId))
        {
            var distinctDays = block.Select(t => t.Date).Distinct().Count();
            if (distinctDays > cap)
            {
                var first = block.First();
                violations.Add(new ConstraintViolation(
                    ViolationKind.MaxConsecutiveDays,
                    first.AgentId,
                    null,
                    block.Key,
                    $"Block has {distinctDays} consecutive days, cap is {cap}."));
            }
        }
    }

    private static void CheckMinPauseHours(CoreScenario scenario, CoreWizardContext context, List<ConstraintViolation> violations)
    {
        var minPause = context.SchedulingMinPauseHours;
        if (minPause <= 0)
        {
            return;
        }

        foreach (var perAgent in scenario.Tokens.GroupBy(t => t.AgentId))
        {
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

    private static void CheckMaxDailyHours(CoreScenario scenario, CoreWizardContext context, List<ConstraintViolation> violations)
    {
        var dayLookup = context.ContractDays
            .ToDictionary(d => (d.AgentId, d.Date));

        foreach (var grouped in scenario.Tokens.GroupBy(t => (t.AgentId, t.Date)))
        {
            var sumHours = (double)grouped.Sum(t => t.TotalHours);
            var cap = dayLookup.TryGetValue(grouped.Key, out var day) && day.MaximumHoursPerDay > 0
                ? day.MaximumHoursPerDay
                : context.SchedulingMaxDailyHours;

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

    private static void CheckMaximumHoursExceeded(CoreScenario scenario, CoreWizardContext context, List<ConstraintViolation> violations)
    {
        foreach (var agent in context.Agents)
        {
            if (agent.MaximumHours <= 0)
            {
                continue;
            }

            var assigned = scenario.Tokens
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

    private static void CheckUnderSupply(CoreScenario scenario, CoreWizardContext context, List<ConstraintViolation> violations)
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
        foreach (var token in scenario.Tokens)
        {
            if (token.ShiftRefId == Guid.Empty)
            {
                continue;
            }

            var key = (token.ShiftRefId, token.Date);
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
    }
}
