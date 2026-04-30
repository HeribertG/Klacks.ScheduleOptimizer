// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Generates reproducible test data (CoreWizardContext) from Story definitions for the token-based engine.
/// Uses seeded RNG for deterministic scenarios across runs and Guid-based IDs so the auction/repair
/// stages can correctly identify shifts (TokenConstraintChecker filters Guid.Empty out as invalid).
/// </summary>
/// <param name="story">Story definition with agent/shift counts and config</param>

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Evaluators;

public static class ScenarioGenerator
{
    private const int DAYS_IN_MONTH = 28;
    private const string BASE_DATE = "2026-04-01";

    public static CoreWizardContext Generate(Story story, int seed = 42)
    {
        var rng = new Random(seed);
        var agents = GenerateAgents(story, rng);
        var shifts = GenerateShifts(story, rng);
        var periodFrom = DateOnly.Parse(BASE_DATE);
        var periodUntil = periodFrom.AddDays(DAYS_IN_MONTH - 1);

        var cfg = story.AgentConfig;
        var breakBlockers = GenerateBreakBlockers(agents, cfg, periodFrom);
        var lockedWorks = GenerateLockedWorks(agents, shifts, cfg, rng);

        var lockedShiftIds = new HashSet<string>(lockedWorks.Select(lw => lw.ShiftRefId.ToString()));
        var openShifts = shifts.Where(s => !lockedShiftIds.Contains(s.Id)).ToList();

        return new CoreWizardContext
        {
            PeriodFrom = periodFrom,
            PeriodUntil = periodUntil,
            Agents = agents,
            Shifts = openShifts,
            SchedulingMaxConsecutiveDays = cfg.MaxConsecutiveDays,
            SchedulingMinPauseHours = cfg.MinRestHours,
            SchedulingMaxOptimalGap = cfg.MaxOptimalGap,
            SchedulingMaxDailyHours = cfg.MaxDailyHours,
            SchedulingMaxWeeklyHours = cfg.MaxWeeklyHours,
            BreakBlockers = breakBlockers,
            LockedWorks = lockedWorks,
        };
    }

    private static IReadOnlyList<CoreBreakBlocker> GenerateBreakBlockers(
        List<CoreAgent> agents, AgentConfig cfg, DateOnly periodFrom)
    {
        if (cfg.BreakBlockerRatio <= 0 || cfg.BreakBlockerDays <= 0)
        {
            return [];
        }

        var blockedCount = (int)Math.Round(agents.Count * cfg.BreakBlockerRatio);
        var blockers = new List<CoreBreakBlocker>(blockedCount);
        var until = periodFrom.AddDays(cfg.BreakBlockerDays - 1);

        for (var i = 0; i < blockedCount && i < agents.Count; i++)
        {
            blockers.Add(new CoreBreakBlocker(agents[i].Id, periodFrom, until, "Vacation"));
        }

        return blockers;
    }

    private static IReadOnlyList<CoreLockedWork> GenerateLockedWorks(
        List<CoreAgent> agents, List<CoreShift> shifts, AgentConfig cfg, Random rng)
    {
        if (cfg.LockedWorkRatio <= 0 || agents.Count == 0 || shifts.Count == 0)
        {
            return [];
        }

        var lockedCount = (int)Math.Round(shifts.Count * cfg.LockedWorkRatio);
        var lockedWorks = new List<CoreLockedWork>(lockedCount);
        var shuffledShifts = shifts.OrderBy(_ => rng.NextDouble()).ToList();
        var assignedDaysByAgent = new Dictionary<string, HashSet<DateOnly>>();

        var agentIndex = 0;
        foreach (var shift in shuffledShifts)
        {
            if (lockedWorks.Count >= lockedCount)
            {
                break;
            }

            if (!DateOnly.TryParse(shift.Date, out var date) || !Guid.TryParse(shift.Id, out var shiftRefId))
            {
                continue;
            }

            var startAt = date.ToDateTime(TimeOnly.Parse(shift.StartTime));
            var endParts = shift.EndTime.Split(':');
            var endHour = int.Parse(endParts[0]);
            var endMinute = int.Parse(endParts[1]);
            var startHour = int.Parse(shift.StartTime.Split(':')[0]);
            var endAt = endHour < startHour
                ? date.AddDays(1).ToDateTime(new TimeOnly(endHour, endMinute))
                : date.ToDateTime(new TimeOnly(endHour, endMinute));

            for (var attempt = 0; attempt < agents.Count; attempt++)
            {
                var agent = agents[(agentIndex + attempt) % agents.Count];
                var agentDays = assignedDaysByAgent.GetValueOrDefault(agent.Id) ?? [];
                if (agentDays.Contains(date))
                {
                    continue;
                }

                agentDays.Add(date);
                assignedDaysByAgent[agent.Id] = agentDays;
                agentIndex = (agentIndex + attempt + 1) % agents.Count;

                lockedWorks.Add(new CoreLockedWork(
                    WorkId: Guid.NewGuid().ToString(),
                    AgentId: agent.Id,
                    Date: date,
                    ShiftTypeIndex: 0,
                    TotalHours: (decimal)shift.Hours,
                    StartAt: startAt,
                    EndAt: endAt,
                    ShiftRefId: shiftRefId,
                    LocationContext: null));
                break;
            }
        }

        return lockedWorks;
    }

    private static List<CoreAgent> GenerateAgents(Story story, Random rng)
    {
        var agents = new List<CoreAgent>();
        var cfg = story.AgentConfig;
        var fdOnlyCount = (int)Math.Round(story.AgentCount * cfg.FdOnlyRatio);
        var maxHoursCapCount = (int)Math.Round(story.AgentCount * cfg.MaxHoursCapRatio);
        var halfSpread = cfg.GuaranteedHoursSpread / 2.0;

        for (var i = 0; i < story.AgentCount; i++)
        {
            var hoursVariation = (1.0 - halfSpread) + rng.NextDouble() * cfg.GuaranteedHoursSpread;
            var guaranteedHours = cfg.GuaranteedHours * hoursVariation;
            var isFdOnly = i < fdOnlyCount;
            var hasMaxHoursCap = i < maxHoursCapCount;
            agents.Add(new CoreAgent(
                Id: $"agent_{i:D4}",
                CurrentHours: 0,
                GuaranteedHours: guaranteedHours,
                MaxConsecutiveDays: cfg.MaxConsecutiveDays,
                MinRestHours: cfg.MinRestHours,
                Motivation: 0.3 + rng.NextDouble() * 0.7,
                MaxDailyHours: cfg.MaxDailyHours,
                MaxWeeklyHours: cfg.MaxWeeklyHours,
                MaxOptimalGap: cfg.MaxOptimalGap)
            {
                PerformsShiftWork = !isFdOnly,
                MaximumHours = hasMaxHoursCap ? guaranteedHours * 1.2 : 0,
            });
        }

        return agents;
    }

    private static List<CoreShift> GenerateShifts(Story story, Random rng)
    {
        var shifts = new List<CoreShift>();
        var shiftTypes = story.ShiftTypes.Count > 0
            ? story.ShiftTypes
            : DefaultShiftTypes();

        var totalRatio = shiftTypes.Sum(st => st.Ratio);
        var baseDate = DateTime.Parse(BASE_DATE);
        var shiftsPerDay = (int)Math.Ceiling((double)story.ShiftCount / DAYS_IN_MONTH);
        var generated = 0;

        for (var day = 0; day < DAYS_IN_MONTH && generated < story.ShiftCount; day++)
        {
            var date = baseDate.AddDays(day).ToString("yyyy-MM-dd");

            foreach (var st in shiftTypes)
            {
                var count = (int)Math.Round(shiftsPerDay * st.Ratio / totalRatio);
                for (var j = 0; j < count && generated < story.ShiftCount; j++)
                {
                    shifts.Add(new CoreShift(
                        Id: Guid.NewGuid().ToString(),
                        Name: st.Name,
                        Date: date,
                        StartTime: st.StartTime,
                        EndTime: st.EndTime,
                        Hours: st.Hours,
                        RequiredAssignments: 1,
                        Priority: st.Priority));
                    generated++;
                }
            }
        }

        return shifts;
    }

    private static List<ShiftTypeConfig> DefaultShiftTypes() =>
    [
        new() { Name = "Fruehdienst", StartTime = "07:00", EndTime = "15:00", Hours = 8, Ratio = 1.0, Priority = 1 },
        new() { Name = "Spaetdienst", StartTime = "15:00", EndTime = "23:00", Hours = 8, Ratio = 1.0, Priority = 1 },
        new() { Name = "Nachtdienst", StartTime = "23:00", EndTime = "07:00", Hours = 8, Ratio = 0.5, Priority = 2 }
    ];
}
