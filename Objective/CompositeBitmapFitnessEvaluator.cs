// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Evolution;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Objective;

/// <summary>
/// Bridges the composite objective into the Harmonizer GA so the W4 background optimiser ranks
/// candidates by the "Gesamtzustand" instead of row-position-weighted harmony. Baseline-parameterised:
/// the runner passes the objective result of the immutable snapshot (the accepted/real state the user
/// sees), and every candidate is scored relative to it. The surviving v1 hard guards (tiered gate,
/// worst-agent hours floor, per-agent blacklist cap) are folded INTO the fitness — an admissible
/// candidate scores its [0,1] composite scalar; an inadmissible one scores a strictly negative,
/// severity-graded value so the GA drives back toward feasibility and never *keeps* a guard-breaking
/// plan as best. The seed always passes (it is the baseline), so the population never degenerates to
/// all-infeasible. Churn stays a runner/Api-layer gate-cap (it needs the persistence-level content key),
/// not folded here. Per-row scores are not meaningful for a plan-level objective and are returned as a
/// length-correct zero vector.
/// </summary>
/// <param name="objective">The composite objective scorer</param>
/// <param name="context">Static scheduling context for the snapshot (agents, shifts, eligibility, preferences, breaks)</param>
/// <param name="baseline">Objective result of the immutable snapshot; the no-regression reference</param>
/// <param name="enforceCoverageFloor">When true (default, recommended for Spitex) UnderSupply may not regress vs baseline</param>
public sealed class CompositeBitmapFitnessEvaluator(
    CompositeObjective objective,
    CoreWizardContext context,
    ObjectiveResult baseline,
    bool enforceCoverageFloor = true) : IBitmapFitnessEvaluator
{
    public FitnessResult Evaluate(HarmonyBitmap bitmap)
    {
        var result = objective.Evaluate(ObjectiveInputBuilder.FromBitmap(bitmap, context));
        var penalty = AdmissibilityPenalty(result);
        var fitness = penalty <= 0.0 ? result.Scalar : -(1.0 + penalty);
        return new FitnessResult(fitness, new double[bitmap.RowCount]);
    }

    /// <summary>
    /// Returns 0 when the candidate breaks no hard guard against the baseline, otherwise a positive
    /// severity-graded magnitude (hard-floor count excesses plus worst-off regressions). Any positive
    /// value makes the candidate inadmissible; the magnitude only orders inadmissible candidates so the
    /// GA prefers the least-infeasible one.
    /// </summary>
    private double AdmissibilityPenalty(ObjectiveResult result)
    {
        var gateExcess =
            Math.Max(0, result.Gate.MandatoryQualMissing - baseline.Gate.MandatoryQualMissing)
            + Math.Max(0, result.Gate.Legality - baseline.Gate.Legality)
            + (enforceCoverageFloor ? Math.Max(0, result.Gate.UnderSupply - baseline.Gate.UnderSupply) : 0);

        var worstHoursExcess = Math.Max(
            0.0,
            (baseline.Diagnostics.WorstStundenabgleich - ObjectiveConstants.GuardEpsilon) - result.Diagnostics.WorstStundenabgleich);

        var blacklistExcess = Math.Max(
            0.0,
            result.Diagnostics.MaxBlacklistFraction - (baseline.Diagnostics.MaxBlacklistFraction + ObjectiveConstants.GuardEpsilon));

        return gateExcess + worstHoursExcess + blacklistExcess;
    }
}
