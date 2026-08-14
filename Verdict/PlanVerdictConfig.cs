// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Verdict;

/// <summary>
/// Tunable settings of the final plan verdict. Every number here is deliberately configuration
/// and not truth — worldwide coverage (owner ruling 2026-08-14) means a country-pack provider
/// feeds these per region later. Validation state of the defaults (research 2026-08-14, see
/// tests/autofill/results/RECHERCHE-GAV-QUOTAVALIDIERUNG-2026-08-14.md): the 48-hour quota, the
/// linear period scaling and the 35-hour legal floor are backed by the Swiss security CBA 2026
/// (art. 15), the ArG and EU directive 2003/88 art. 16a; the 72-hour quota is a deliberate
/// quality setting WITHOUT a legal source. The worldwide legal floor when no regional value is
/// configured is ILO C14/C106: 24 contiguous rest hours per 7-day period.
/// </summary>
/// <param name="RestQuotas">Catalogue of period-scaled rest-window debts. The entries are ALTERNATIVE rule configurations, never cumulative: each agent is judged only against the entry matching the agent's own configured package rest (a flawless five-on-two-off rhythm has 64-hour gaps and would structurally fail a cumulative 72-hour quota)</param>
/// <param name="ReferencePeriodDays">Period length the quota counts are stated for</param>
/// <param name="LegalMinimumRestHours">Absolute floor below which a single package gap always caps the verdict. Default 35 is the Swiss/EU weekly rest (24 h Sunday + 11 h daily rest); regional overrides expected (e.g. IL 36 h), the ILO world floor is 24 h</param>
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

    /// <summary>
    /// Resolves the rest-window quota for an agent's configured package rest. Catalogue entries
    /// are exact anchors; a window between two anchors gets its owed count linearly interpolated,
    /// a window outside the anchor range takes the nearest anchor's count — so no configured
    /// package rest ever falls silently out of the quota judgement.
    /// </summary>
    /// <param name="windowHours">Package rest of the judged agent in hours (MinRestDays × 24)</param>
    public RestWindowQuota? QuotaFor(double windowHours)
    {
        if (windowHours <= 0 || RestQuotas.Count == 0)
        {
            return null;
        }

        var anchors = RestQuotas.OrderBy(q => q.WindowHours).ToList();
        if (windowHours <= anchors[0].WindowHours)
        {
            return new RestWindowQuota(windowHours, anchors[0].WindowsPerReferencePeriod);
        }

        if (windowHours >= anchors[^1].WindowHours)
        {
            return new RestWindowQuota(windowHours, anchors[^1].WindowsPerReferencePeriod);
        }

        for (var i = 0; i + 1 < anchors.Count; i++)
        {
            var lower = anchors[i];
            var upper = anchors[i + 1];
            if (windowHours > upper.WindowHours)
            {
                continue;
            }

            var span = upper.WindowHours - lower.WindowHours;
            var share = span <= 0 ? 0 : (windowHours - lower.WindowHours) / span;
            var windows = lower.WindowsPerReferencePeriod
                + (share * (upper.WindowsPerReferencePeriod - lower.WindowsPerReferencePeriod));
            return new RestWindowQuota(windowHours, windows);
        }

        return null;
    }
}
