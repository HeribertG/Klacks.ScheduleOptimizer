// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution.Metrics;

/// <summary>
/// Multi-dimensional metric vector for one Wizard run. Used as a Pareto-style baseline to detect
/// tuning regressions across multiple objectives (coverage, fairness, mix) instead of a single
/// aggregate score. Persisted as JSON in the regression test baseline.
/// </summary>
/// <param name="CoveragePercent">Filled slots / total slots, in [0..1]</param>
/// <param name="TargetReachedPercent">Agents with (Hours + Surcharges) &gt;= GuaranteedHours / total agents, in [0..1]</param>
/// <param name="SlotGini">Gini coefficient of slot count per agent. 0 = perfectly fair, 1 = one agent has all</param>
/// <param name="ShiftTypeEntropyAvg">Avg Shannon entropy (base 2) of shift-type distribution per agent. 0 = single type, log2(3) ~ 1.585 = perfect mix</param>
/// <param name="Stage1EscalationCount">Number of Stage-1 soft constraint relaxations logged during the run</param>
/// <param name="MaxConsecutiveBlockLen">Longest consecutive-day block of any agent across the plan</param>
/// <param name="RosterFidelityInversionRate">
/// Fraction of roster pairs (higher-priority agent vs lower-priority agent) where the higher one
/// ends up with a worse relative target-hours deviation. 0 = perfect top-down fidelity (the top of
/// the roster is always at least as accurate as everyone below), 1 = fully inverted.
/// </param>
public sealed record WizardMetricsSnapshot(
    double CoveragePercent,
    double TargetReachedPercent,
    double SlotGini,
    double ShiftTypeEntropyAvg,
    int Stage1EscalationCount,
    int MaxConsecutiveBlockLen,
    double RosterFidelityInversionRate);
