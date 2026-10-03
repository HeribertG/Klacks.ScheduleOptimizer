// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

/// <summary>Bit set of the classifications a single agent-day carries; one day can be Early and Night at once.</summary>
[Flags]
public enum RuleDayFlags : byte
{
    None = 0,
    Work = 1,
    Early = 2,
    Late = 4,
    Night = 8,
}
