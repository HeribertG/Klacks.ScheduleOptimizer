// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Evaluates hard and soft scheduling constraints against a scenario.
/// Port of constraint-engine.ts — checks daily hours, weekly hours, consecutive days,
/// time overlap, rest periods, fairness, overtime, motivation, shift gaps and consistency.
/// </summary>
/// <param name="assignments">List of shift-to-agent assignments</param>
/// <param name="shifts">Available shifts with times</param>
/// <param name="agents">Agents with constraint limits</param>

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Engine;

public record ConstraintViolation(string Type, string AgentId, string Description);

public static class ConstraintEngine
{
    public static List<ConstraintViolation> EvaluateHardViolations(
        List<CoreAssignment> assignments,
        List<CoreShift> shifts,
        List<CoreAgent> agents)
    {
        var shiftMap = shifts.ToDictionary(s => s.Id);
        var agentMap = agents.ToDictionary(a => a.Id);
        var (agentDailyHours, agentTimeSlots) = BuildAgentAggregations(assignments, shiftMap);

        var violations = new List<ConstraintViolation>();
        violations.AddRange(CheckDailyHours(agentDailyHours, agentMap));
        violations.AddRange(CheckWeeklyHours(agentDailyHours, agentMap));
        violations.AddRange(CheckConsecutiveDays(agentDailyHours, agentMap));
        violations.AddRange(CheckTimeOverlap(agentTimeSlots));
        violations.AddRange(CheckRestPeriod(agentTimeSlots, agentMap));
        return violations;
    }

    public static List<ConstraintViolation> EvaluateSoftViolations(
        List<CoreAssignment> assignments,
        List<CoreShift> shifts,
        List<CoreAgent> agents)
    {
        var shiftMap = shifts.ToDictionary(s => s.Id);

        var agentHours = new Dictionary<string, double>();
        var agentDailySlots = new Dictionary<string, Dictionary<string, List<(string Start, string End, string Name)>>>();

        foreach (var a in assignments)
        {
            if (!shiftMap.TryGetValue(a.ShiftId, out var shift)) continue;
            var dateKey = shift.Date.Split('T')[0];

            agentHours[a.AgentId] = agentHours.GetValueOrDefault(a.AgentId) + shift.Hours;

            if (!agentDailySlots.ContainsKey(a.AgentId))
                agentDailySlots[a.AgentId] = [];
            if (!agentDailySlots[a.AgentId].ContainsKey(dateKey))
                agentDailySlots[a.AgentId][dateKey] = [];
            agentDailySlots[a.AgentId][dateKey].Add((shift.StartTime, shift.EndTime, shift.Name));
        }

        var agentMap = agents.ToDictionary(a => a.Id);

        var violations = new List<ConstraintViolation>();
        violations.AddRange(CheckFairness(agentHours));
        violations.AddRange(CheckOvertime(agents, agentHours));
        violations.AddRange(CheckLowMotivation(assignments));
        violations.AddRange(CheckShiftGap(agentDailySlots, agentMap));
        violations.AddRange(CheckShiftConsistency(agentDailySlots));
        return violations;
    }

    private static (Dictionary<string, Dictionary<string, double>>, Dictionary<string, List<(string Date, string Start, string End)>>)
        BuildAgentAggregations(List<CoreAssignment> assignments, Dictionary<string, CoreShift> shiftMap)
    {
        var agentDailyHours = new Dictionary<string, Dictionary<string, double>>();
        var agentTimeSlots = new Dictionary<string, List<(string Date, string Start, string End)>>();

        foreach (var assignment in assignments)
        {
            if (!shiftMap.TryGetValue(assignment.ShiftId, out var shift)) continue;
            var dateKey = shift.Date.Split('T')[0];

            if (!agentDailyHours.ContainsKey(assignment.AgentId))
                agentDailyHours[assignment.AgentId] = [];
            var dailyMap = agentDailyHours[assignment.AgentId];
            dailyMap[dateKey] = dailyMap.GetValueOrDefault(dateKey) + shift.Hours;

            if (!agentTimeSlots.ContainsKey(assignment.AgentId))
                agentTimeSlots[assignment.AgentId] = [];
            agentTimeSlots[assignment.AgentId].Add((dateKey, shift.StartTime, shift.EndTime));
        }

        return (agentDailyHours, agentTimeSlots);
    }

    private static List<ConstraintViolation> CheckDailyHours(
        Dictionary<string, Dictionary<string, double>> agentDailyHours,
        Dictionary<string, CoreAgent> agentMap)
    {
        var violations = new List<ConstraintViolation>();
        foreach (var (agentId, dailyMap) in agentDailyHours)
        {
            if (!agentMap.TryGetValue(agentId, out var agent)) continue;
            foreach (var (dateKey, hours) in dailyMap)
            {
                if (hours > agent.MaxDailyHours)
                    violations.Add(new("hard", agentId, $"Agent exceeds {agent.MaxDailyHours}h on {dateKey} ({hours}h)"));
            }
        }
        return violations;
    }

    private static List<ConstraintViolation> CheckWeeklyHours(
        Dictionary<string, Dictionary<string, double>> agentDailyHours,
        Dictionary<string, CoreAgent> agentMap)
    {
        var violations = new List<ConstraintViolation>();
        foreach (var (agentId, dailyMap) in agentDailyHours)
        {
            if (!agentMap.TryGetValue(agentId, out var agent)) continue;
            var weeklyHours = new Dictionary<string, double>();
            foreach (var (dateKey, hours) in dailyMap)
            {
                var wk = GetWeekKey(dateKey);
                weeklyHours[wk] = weeklyHours.GetValueOrDefault(wk) + hours;
            }
            foreach (var (weekKey, hours) in weeklyHours)
            {
                if (hours > agent.MaxWeeklyHours)
                    violations.Add(new("hard", agentId, $"Agent exceeds {agent.MaxWeeklyHours}h in week {weekKey} ({hours}h)"));
            }
        }
        return violations;
    }

    private static List<ConstraintViolation> CheckConsecutiveDays(
        Dictionary<string, Dictionary<string, double>> agentDailyHours,
        Dictionary<string, CoreAgent> agentMap)
    {
        var violations = new List<ConstraintViolation>();
        foreach (var (agentId, dailyMap) in agentDailyHours)
        {
            if (!agentMap.TryGetValue(agentId, out var agent)) continue;
            var workedDates = dailyMap.Keys.OrderBy(d => d).ToList();
            var consecutive = 1;
            for (var i = 1; i < workedDates.Count; i++)
            {
                var prev = DateTime.Parse(workedDates[i - 1]);
                var curr = DateTime.Parse(workedDates[i]);
                if ((curr - prev).TotalDays == 1)
                {
                    consecutive++;
                    if (consecutive > agent.MaxConsecutiveDays)
                        violations.Add(new("hard", agentId, $"Agent exceeds max consecutive days ({consecutive} > {agent.MaxConsecutiveDays})"));
                }
                else
                {
                    consecutive = 1;
                }
            }
        }
        return violations;
    }

    private static List<ConstraintViolation> CheckTimeOverlap(
        Dictionary<string, List<(string Date, string Start, string End)>> agentTimeSlots)
    {
        var violations = new List<ConstraintViolation>();
        foreach (var (agentId, slots) in agentTimeSlots)
        {
            for (var i = 0; i < slots.Count; i++)
            {
                for (var j = i + 1; j < slots.Count; j++)
                {
                    if (slots[i].Date == slots[j].Date &&
                        TimeSlotsOverlap(slots[i].Start, slots[i].End, slots[j].Start, slots[j].End))
                    {
                        violations.Add(new("hard", agentId, $"Agent double-booked on {slots[i].Date}"));
                    }
                }
            }
        }
        return violations;
    }

    private static List<ConstraintViolation> CheckRestPeriod(
        Dictionary<string, List<(string Date, string Start, string End)>> agentTimeSlots,
        Dictionary<string, CoreAgent> agentMap)
    {
        var violations = new List<ConstraintViolation>();
        foreach (var (agentId, slots) in agentTimeSlots)
        {
            if (!agentMap.TryGetValue(agentId, out var agent)) continue;
            var sorted = slots.OrderBy(s => s.Date).ThenBy(s => s.Start).ToList();
            for (var i = 1; i < sorted.Count; i++)
            {
                var prev = sorted[i - 1];
                var curr = sorted[i];
                if (prev.Date == curr.Date) continue;

                var prevEnd = DateTime.Parse($"{prev.Date}T{prev.End}");
                if (string.Compare(prev.End, prev.Start, StringComparison.Ordinal) <= 0)
                    prevEnd = prevEnd.AddDays(1);
                var currStart = DateTime.Parse($"{curr.Date}T{curr.Start}");
                var pauseHours = (currStart - prevEnd).TotalHours;
                if (pauseHours > 0 && pauseHours < agent.MinRestHours)
                    violations.Add(new("hard", agentId, $"Agent rest period too short ({pauseHours:F1}h < {agent.MinRestHours}h)"));
            }
        }
        return violations;
    }

    private static List<ConstraintViolation> CheckFairness(Dictionary<string, double> agentHours)
    {
        if (agentHours.Count <= 1) return [];
        var hours = agentHours.Values.ToList();
        var avg = hours.Average();
        var maxDev = hours.Max(h => Math.Abs(h - avg));
        if (avg > 0 && maxDev / avg > SchedulingConstants.FAIRNESS_MAX_DEVIATION_RATIO)
        {
            var worst = agentHours.MaxBy(kv => Math.Abs(kv.Value - avg));
            return [new("soft", worst.Key, $"Hour distribution unfair (max deviation {maxDev / avg * 100:F0}%)")];
        }
        return [];
    }

    private static List<ConstraintViolation> CheckOvertime(List<CoreAgent> agents, Dictionary<string, double> agentHours)
    {
        var violations = new List<ConstraintViolation>();
        foreach (var agent in agents)
        {
            var totalHours = agentHours.GetValueOrDefault(agent.Id) + agent.CurrentHours;
            if (agent.GuaranteedHours > 0 && totalHours > agent.GuaranteedHours * SchedulingConstants.OVERTIME_THRESHOLD_FACTOR)
                violations.Add(new("soft", agent.Id, $"Agent approaching overtime ({totalHours:F1}h / {agent.GuaranteedHours}h guaranteed)"));
        }
        return violations;
    }

    private static List<ConstraintViolation> CheckLowMotivation(List<CoreAssignment> assignments)
    {
        return assignments
            .Where(a => a.MotivationScore < SchedulingConstants.LOW_MOTIVATION_THRESHOLD)
            .Select(a => new ConstraintViolation("soft", a.AgentId, $"Low motivation for shift {a.ShiftId} ({a.MotivationScore * 100:F0}%)"))
            .ToList();
    }

    private static List<ConstraintViolation> CheckShiftGap(
        Dictionary<string, Dictionary<string, List<(string Start, string End, string Name)>>> agentDailySlots,
        Dictionary<string, CoreAgent> agentMap)
    {
        var violations = new List<ConstraintViolation>();
        foreach (var (agentId, dailyMap) in agentDailySlots)
        {
            if (!agentMap.TryGetValue(agentId, out var agent)) continue;
            foreach (var (dateKey, slots) in dailyMap)
            {
                if (slots.Count < 2) continue;
                var sorted = slots.OrderBy(s => s.Start).ToList();
                for (var i = 1; i < sorted.Count; i++)
                {
                    var gap = TimeGapHours(sorted[i - 1].End, sorted[i].Start);
                    if (gap > agent.MaxOptimalGap)
                        violations.Add(new("soft", agentId, $"Gap between shifts on {dateKey} is {gap:F1}h (max {agent.MaxOptimalGap}h)"));
                }
            }
        }
        return violations;
    }

    private static List<ConstraintViolation> CheckShiftConsistency(
        Dictionary<string, Dictionary<string, List<(string Start, string End, string Name)>>> agentDailySlots)
    {
        var violations = new List<ConstraintViolation>();
        foreach (var (agentId, dailyMap) in agentDailySlots)
        {
            var dates = dailyMap.Keys.OrderBy(d => d).ToList();
            for (var i = 1; i < dates.Count; i++)
            {
                var prev = DateTime.Parse(dates[i - 1]);
                var curr = DateTime.Parse(dates[i]);
                if ((curr - prev).TotalDays != 1) continue;

                var prevName = dailyMap[dates[i - 1]][0].Name;
                var currName = dailyMap[dates[i]][0].Name;
                if (prevName != currName)
                    violations.Add(new("soft", agentId, $"Shift inconsistency: {prevName} on {dates[i - 1]} vs {currName} on {dates[i]}"));
            }
        }
        return violations;
    }

    private static bool TimeSlotsOverlap(string start1, string end1, string start2, string end2)
        => string.Compare(start1, end2, StringComparison.Ordinal) < 0
        && string.Compare(start2, end1, StringComparison.Ordinal) < 0;

    private static string GetWeekKey(string dateStr)
    {
        var d = DateTime.Parse(dateStr);
        var day = (int)d.DayOfWeek;
        var diff = d.Day - day + (day == 0 ? -6 : 1);
        var monday = new DateTime(d.Year, d.Month, 1).AddDays(diff - 1);
        return monday.ToString("yyyy-MM-dd");
    }

    private static double TimeGapHours(string end, string start)
    {
        var ep = end.Split(':').Select(int.Parse).ToArray();
        var sp = start.Split(':').Select(int.Parse).ToArray();
        return (sp[0] * 60.0 + sp[1] - ep[0] * 60.0 - ep[1]) / 60.0;
    }
}
