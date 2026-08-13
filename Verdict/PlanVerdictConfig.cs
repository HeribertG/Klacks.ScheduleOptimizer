// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Verdict;

/// <summary>
/// Tunable settings of the final plan verdict. Every number here is a first, visible setting —
/// deliberately configuration and not truth: the default rest quotas follow the owner sketch of
/// 2026-08-13 ("about four 48-hour windows per month, three of 72 hours, halved over a fortnight")
/// and are NOT yet validated against the binding collective agreement; validate them before the
/// verdict is ever allowed to choose in production.
/// </summary>
/// <param name="RestQuotas">Catalogue of period-scaled rest-window debts. The entries are ALTERNATIVE rule configurations, never cumulative: each agent is judged only against the entry matching the agent's own configured package rest (a flawless five-on-two-off rhythm has 64-hour gaps and would structurally fail a cumulative 72-hour quota)</param>
/// <param name="ReferencePeriodDays">Period length the quota counts are stated for</param>
/// <param name="LegalMinimumRestHours">Absolute floor below which a single gap always caps the verdict</param>
/// <param name="ScratchPenalty">Soft-score deduction per scratch finding</param>
/// <param name="ScratchPenaltyCeiling">Upper bound of the total scratch deduction</param>
/// <param name="QuotaShortfallCapFloor">Cap the score falls to when a quota is fully missed; the cap eases linearly to 1 as fulfilment reaches 1</param>
/// <param name="LegalBreachCap">Hard cap of the score once any gap undercuts the legal minimum</param>
/// <param name="WeightCompactness">Soft weight of the short-package-free term</param>
/// <param name="WeightPurity">Soft weight of the single-kind-package term</param>
/// <param name="WeightLengthDiscipline">Soft weight of the no-overlong-package term</param>
/// <param name="WeightKindFairness">Soft weight of the shift-kind fairness term</param>
public sealed record PlanVerdictConfig
{
    public IReadOnlyList<RestWindowQuota> RestQuotas { get; init; } =
    [
        new RestWindowQuota(WindowHours: 48, WindowsPerReferencePeriod: 4),
        new RestWindowQuota(WindowHours: 72, WindowsPerReferencePeriod: 3),
    ];

    public int ReferencePeriodDays { get; init; } = 28;

    public double LegalMinimumRestHours { get; init; } = 35;

    public double ScratchPenalty { get; init; } = 0.05;

    public double ScratchPenaltyCeiling { get; init; } = 0.25;

    public double QuotaShortfallCapFloor { get; init; } = 0.5;

    public double LegalBreachCap { get; init; } = 0.25;

    public double WeightCompactness { get; init; } = 0.35;

    public double WeightPurity { get; init; } = 0.25;

    public double WeightLengthDiscipline { get; init; } = 0.15;

    public double WeightKindFairness { get; init; } = 0.25;
}
