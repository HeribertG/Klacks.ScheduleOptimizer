// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Aggregate of everything the planning rules need about one agent-day: classification flags, number of
/// worked segments, number of night segments and the longest segment durations (the four longest are kept,
/// which makes ShiftExceedingHours exact unless more than four segments of one day exceed the threshold).
/// A free day and a break day are both the default value - a break counts as free. The Night flag (sequence and
/// fairness rules) needs more than the agent's NightRuleMinOverlapMinutes of overlap, NightSegmentCount
/// (PeriodCount) counts every segment with any overlap.
/// </summary>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public struct RuleDay
{
    public const int TrackedSegmentDurations = 4;

    private SegmentDurationBuffer _durations;
    private byte _trackedDurations;

    public RuleDayFlags Flags { readonly get; private set; }

    public int SegmentCount { readonly get; private set; }

    public int NightSegmentCount { readonly get; private set; }

    public static RuleDay Free => default;

    public readonly bool IsWork => SegmentCount > 0;

    public readonly bool Has(RuleShiftKind kind) => kind switch
    {
        RuleShiftKind.Work => SegmentCount > 0,
        RuleShiftKind.Early => (Flags & RuleDayFlags.Early) != 0,
        RuleShiftKind.Late => (Flags & RuleDayFlags.Late) != 0,
        RuleShiftKind.Night => (Flags & RuleDayFlags.Night) != 0,
        _ => false,
    };

    public readonly int CountSegmentsLongerThan(decimal minutes)
    {
        var count = 0;
        for (var i = 0; i < _trackedDurations; i++)
        {
            if (_durations[i] > minutes)
            {
                count++;
            }
        }

        return count;
    }

    public readonly RuleDay WithSegment(in RuleSegment segment, RuleAgent agent)
    {
        var copy = this;
        copy.AddSegment(segment, agent);
        return copy;
    }

    private void AddSegment(in RuleSegment segment, RuleAgent agent)
    {
        SegmentCount++;
        var flags = Flags | RuleDayFlags.Work;
        if (segment.ShiftTypeIndex == RuleTimeConstants.EarlyShiftTypeIndex)
        {
            flags |= RuleDayFlags.Early;
        }
        else if (segment.ShiftTypeIndex == RuleTimeConstants.LateShiftTypeIndex)
        {
            flags |= RuleDayFlags.Late;
        }

        if (RuleSegmentClassifier.IsNight(segment, agent.NightWindow))
        {
            NightSegmentCount++;
        }

        if (RuleSegmentClassifier.IsNight(segment, agent.NightWindow, agent.NightRuleMinOverlapMinutes))
        {
            flags |= RuleDayFlags.Night;
        }

        Flags = flags;
        TrackDuration(RuleSegmentClassifier.DurationMinutes(segment));
    }

    private void TrackDuration(int minutes)
    {
        if (_trackedDurations < TrackedSegmentDurations)
        {
            _durations[_trackedDurations] = minutes;
            _trackedDurations++;
            return;
        }

        var shortest = 0;
        for (var i = 1; i < TrackedSegmentDurations; i++)
        {
            if (_durations[i] < _durations[shortest])
            {
                shortest = i;
            }
        }

        if (minutes > _durations[shortest])
        {
            _durations[shortest] = minutes;
        }
    }
}
