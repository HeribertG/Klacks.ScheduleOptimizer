// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Fixed inline storage for the longest segment durations of one agent-day, so a RuleDay stays a plain
/// value without a heap array.
/// </summary>

using System.Runtime.CompilerServices;

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

[InlineArray(RuleDay.TrackedSegmentDurations)]
public struct SegmentDurationBuffer
{
    private int _element;
}
