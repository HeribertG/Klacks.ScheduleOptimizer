// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

/// <summary>Window a team-fairness rule compares over; calendar windows are clipped to the planning period.</summary>
public enum FairnessWindow
{
    PlanPeriod,
    Week,
    Month,
}
