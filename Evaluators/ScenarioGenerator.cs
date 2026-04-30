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
        return new CoreWizardContext
        {
            PeriodFrom = periodFrom,
            PeriodUntil = periodUntil,
            Agents = agents,
            Shifts = shifts,
            SchedulingMaxConsecutiveDays = cfg.MaxConsecutiveDays,
            SchedulingMinPauseHours = cfg.MinRestHours,
            SchedulingMaxOptimalGap = cfg.MaxOptimalGap,
            SchedulingMaxDailyHours = cfg.MaxDailyHours,
            SchedulingMaxWeeklyHours = cfg.MaxWeeklyHours,
        };
    }

    private static List<CoreAgent> GenerateAgents(Story story, Random rng)
    {
        var agents = new List<CoreAgent>();
        var cfg = story.AgentConfig;

        for (var i = 0; i < story.AgentCount; i++)
        {
            var hoursVariation = 0.8 + rng.NextDouble() * 0.4;
            agents.Add(new CoreAgent(
                Id: $"agent_{i:D4}",
                CurrentHours: 0,
                GuaranteedHours: cfg.GuaranteedHours * hoursVariation,
                MaxConsecutiveDays: cfg.MaxConsecutiveDays,
                MinRestHours: cfg.MinRestHours,
                Motivation: 0.3 + rng.NextDouble() * 0.7,
                MaxDailyHours: cfg.MaxDailyHours,
                MaxWeeklyHours: cfg.MaxWeeklyHours,
                MaxOptimalGap: cfg.MaxOptimalGap));
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
