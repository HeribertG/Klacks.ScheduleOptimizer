// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Objective;

/// <summary>
/// Tiered feasibility gate. Severity lives HERE, not in a weight: mandatory-qualification and
/// legality are SEPARATE hard floors, never folded into one fungible violation count. This fixes
/// the adversarial-grill "Befund 1" — under a flat count, swapping a mandatory-qual miss for an
/// oversupply leaves the count unchanged and the gate would pass; with separate floors the
/// qualification regression is caught. Supply counts are exposed but treated as soft (priority of
/// coverage is arbitrated by the S_Fehler weight, not the gate) unless the operator enables the
/// optional coverage floor.
/// </summary>
/// <param name="MandatoryQualMissing">Count of QualificationMissing violations (mandatory tier — un-negotiable)</param>
/// <param name="Legality">Count of legality violations (MinPause/MaxConsecutive/MaxDaily/MaximumHours/WorkOnDay/PerformsShiftWork/PerDayKeyword/BreakBlocker)</param>
/// <param name="UnderSupply">Count of UnderSupply violations (soft; coverage)</param>
/// <param name="OverSupply">Count of OverSupply violations (soft; coverage)</param>
public sealed record GateResult(
    int MandatoryQualMissing,
    int Legality,
    int UnderSupply,
    int OverSupply)
{
    /// <summary>True when the plan carries no mandatory-qualification and no legality violation at all (absolute feasibility, independent of any baseline).</summary>
    public bool IsFeasibleStandalone => MandatoryQualMissing == 0 && Legality == 0;

    /// <summary>
    /// True if this gate regresses against a baseline gate on any hard floor (the W4 keep-if-better
    /// rule: a candidate may never raise a hard floor above the accepted baseline). With
    /// <paramref name="enforceCoverageFloor"/> the UnderSupply count is treated as an additional hard
    /// floor (recommended for Spitex; coverage may not regress vs baseline).
    /// </summary>
    /// <param name="baseline">The accepted/real-state gate to compare against</param>
    /// <param name="enforceCoverageFloor">When true, also reject an increase in UnderSupply</param>
    public bool RegressesAgainst(GateResult baseline, bool enforceCoverageFloor = false)
        => MandatoryQualMissing > baseline.MandatoryQualMissing
            || Legality > baseline.Legality
            || (enforceCoverageFloor && UnderSupply > baseline.UnderSupply);
}
