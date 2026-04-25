// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Derives the shift-type index (0=FD, 1=SD, 2=ND) from the shift start time.
/// Used during initial population because CoreShift stores only start/end time strings.
/// </summary>
public static class ShiftTypeInference
{
    /// <summary>
    /// Rules:
    /// - Start in [04:00, 12:00) → 0 (Frühdienst)
    /// - Start in [12:00, 20:00) → 1 (Spätdienst)
    /// - Else                    → 2 (Nachtdienst)
    /// </summary>
    public static int FromStartTime(TimeOnly start)
    {
        var hour = start.Hour;
        if (hour >= 4 && hour < 12)
        {
            return 0;
        }

        if (hour >= 12 && hour < 20)
        {
            return 1;
        }

        return 2;
    }

    public static int FromStartTimeString(string startTime)
    {
        return TimeOnly.TryParse(startTime, out var time) ? FromStartTime(time) : 0;
    }
}
