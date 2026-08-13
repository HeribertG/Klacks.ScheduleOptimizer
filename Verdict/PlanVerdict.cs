// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Verdict;

/// <summary>
/// The final fuzzy balance of one finished plan. Hard rules stay hard everywhere plans are BUILT;
/// this record exists only at the very end, where finished candidates are compared — the softening
/// happens last (owner ruling 2026-08-13). Rule compliance never adds to the score: it only caps
/// it, so the soft terms cannot be double-rewarded for the structure the quotas already demand.
/// </summary>
/// <param name="Score">Final score in [0, 1] after scratch deductions and caps</param>
/// <param name="Zone">Worst zone the plan touched (clean, scratched, quota shortfall, legal breach)</param>
/// <param name="SoftScore">Weighted soft score before deductions and caps</param>
/// <param name="QuotaCap">Cap derived from the weakest quota fulfilment</param>
/// <param name="MinQuotaFulfillment">Weakest fuzzy quota fulfilment over all agents and quotas</param>
/// <param name="Terms">Explained soft ingredients of the score</param>
/// <param name="Quotas">Per-agent, per-quota fulfilment records</param>
/// <param name="Findings">Concrete rest gaps that fell short (scratches and legal breaches)</param>
public sealed record PlanVerdict(
    double Score,
    VerdictZone Zone,
    double SoftScore,
    double QuotaCap,
    double MinQuotaFulfillment,
    IReadOnlyList<VerdictTerm> Terms,
    IReadOnlyList<QuotaFulfillment> Quotas,
    IReadOnlyList<VerdictFinding> Findings);
