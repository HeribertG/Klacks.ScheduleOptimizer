// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

/// <summary>What a team-fairness rule compares across the agents of its scope (all counted in days).</summary>
public enum FairnessMetric
{
    NightDays,
    WeekendDays,
    WorkedDays,
}
