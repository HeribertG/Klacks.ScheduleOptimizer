// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Search;

public enum DeterministicSearchStopReason
{
    LocalOptimum,
    NoImprovementLimit,
    IterationLimit,
    EvaluationLimit,
    WallClockBudget,
}
