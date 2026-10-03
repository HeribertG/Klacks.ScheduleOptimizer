// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>Time and scale constants shared by the planning-rule evaluator.</summary>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public static class RuleTimeConstants
{
    public const int MinutesPerHour = 60;

    public const int MinutesPerDay = 24 * MinutesPerHour;

    public const int DaysPerWeek = 7;

    public const decimal FullWorkloadPercent = 100m;

    public const int UnknownShiftTypeIndex = -1;

    public const int EarlyShiftTypeIndex = 0;

    public const int LateShiftTypeIndex = 1;

    public const int NightShiftTypeIndex = 2;
}
