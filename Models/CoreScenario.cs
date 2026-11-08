// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Constraints.Keywords;

/// <summary>
/// A complete scheduling solution (token-based genome).
/// Fitness is broken down into 5 lexicographically ordered stages.
/// </summary>

namespace Klacks.ScheduleOptimizer.Models;

public class CoreScenario
{
    public string Id { get; set; } = string.Empty;

    public List<CoreToken> Tokens { get; set; } = [];

    /// <summary>Aggregate fitness (stage-weighted sum, used for telemetry).</summary>
    public double Fitness { get; set; }

    public int HardViolations { get; set; }

    /// <summary>Stage 0: hard-constraint violations (count, must minimize to 0).</summary>
    public int FitnessStage0 { get; set; }

    /// <summary>
    /// Legality subset of Stage 0: every hard violation except UnderSupply. Compared before the
    /// full Stage-0 count so that an unstaffed slot can never be bought by breaking a work rule —
    /// a plan stays legal first, covered second.
    /// </summary>
    public int FitnessStage0Legality { get; set; }

    /// <summary>
    /// Tokens held by stand-ins (placeholders). Fewer is better, compared right before Stage 1: a stand-in fills only
    /// what real people cannot, so no plan buys hours or soft goals by handing a coverable slot to a stand-in. Coverage
    /// and legality rank above it, so a stand-in still beats an open slot. Always 0 without stand-ins.
    /// </summary>
    public int FitnessStandInTokens { get; set; }

    /// <summary>Stage 1: GuaranteedHours coverage top-down (0..1, higher is better).</summary>
    public double FitnessStage1 { get; set; }

    /// <summary>
    /// Keyword-wish tiers as counts: broken restrictions (FREE, OnlyX, NoX, compared lexicographically) and unfulfilled
    /// positive wishes. In Soft keyword mode the broken tiers rank right after coverage and before Stage 1, in Hard mode
    /// after Stage 1; the unfulfilled wishes rank after Stage 1 and before Stage 2 in both modes. Empty without schedule
    /// commands.
    /// </summary>
    public KeywordWishTally FitnessKeywordWishes { get; set; }

    /// <summary>Stage 2: FullTime / MaxPossible coverage top-down (0..1).</summary>
    public double FitnessStage2 { get; set; }

    /// <summary>Stage 3: soft constraints (block ordering, preferences, location). 0..1.</summary>
    public double FitnessStage3 { get; set; }

    /// <summary>Stage 4: cosmetic optimizations (fairness, MinimumHours). 0..1.</summary>
    public double FitnessStage4 { get; set; }

    /// <summary>Keyword mode of the run that produced the scenario; the apply partition reads it.</summary>
    public KeywordMode KeywordMode { get; set; } = KeywordMode.Hard;

    /// <summary>
    /// Ids of every agent the run planned, also those that hold no token; the keyword-wish count after apply reads
    /// it so an unfulfilled wish of an agent without a shift is counted. Empty when the producer did not set it.
    /// </summary>
    public List<string> PlannedAgentIds { get; set; } = [];
}
