// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Classifies a single segment: duration in minutes and whether it is night work. The duration and the
/// night-overlap arithmetic are taken over unchanged from the API CounterRuleEvaluator: both the segment
/// and the window may wrap midnight, so the window is tested at its own offset and shifted by one day in
/// either direction, and any overlap of more than zero minutes is night. Only without a window, or without
/// clock times on the segment, night falls back to ShiftTypeIndex 2.
/// </summary>

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public static class RuleSegmentClassifier
{
    public static int DurationMinutes(in RuleSegment segment)
    {
        if (!segment.HasClockTimes)
        {
            return (int)Math.Round(segment.Hours * RuleTimeConstants.MinutesPerHour, MidpointRounding.AwayFromZero);
        }

        var start = ToMinutes(segment.Start!.Value);
        var end = ToMinutes(segment.End!.Value);
        return end > start ? end - start : RuleTimeConstants.MinutesPerDay - start + end;
    }

    public static bool IsNight(in RuleSegment segment, CoreNightWindow? window)
    {
        if (window is null || !segment.HasClockTimes)
        {
            return segment.ShiftTypeIndex == RuleTimeConstants.NightShiftTypeIndex;
        }

        return NightOverlapMinutes(segment.Start!.Value, segment.End!.Value, window.Value.Start, window.Value.End) > 0;
    }

    public static int NightOverlapMinutes(TimeOnly segmentStart, TimeOnly segmentEnd, TimeOnly windowStart, TimeOnly windowEnd)
    {
        var segStart = ToMinutes(segmentStart);
        var segEnd = ToMinutes(segmentEnd);
        if (segEnd <= segStart)
        {
            segEnd += RuleTimeConstants.MinutesPerDay;
        }

        var winStart = ToMinutes(windowStart);
        var winEnd = ToMinutes(windowEnd);
        if (winEnd <= winStart)
        {
            winEnd += RuleTimeConstants.MinutesPerDay;
        }

        var overlap = 0;
        for (var shift = -RuleTimeConstants.MinutesPerDay; shift <= RuleTimeConstants.MinutesPerDay; shift += RuleTimeConstants.MinutesPerDay)
        {
            var start = Math.Max(segStart, winStart + shift);
            var end = Math.Min(segEnd, winEnd + shift);
            overlap = Math.Max(overlap, end - start);
        }

        return overlap;
    }

    private static int ToMinutes(TimeOnly time) => (int)time.ToTimeSpan().TotalMinutes;
}
