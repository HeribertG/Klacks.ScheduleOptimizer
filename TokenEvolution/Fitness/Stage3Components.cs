// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

/// <summary>
/// Per-dimension breakdown of the Stage-3 soft-constraint score, each value in [0..1] where 1 is best.
/// Recomposing these with the evaluator's Stage3 weights reproduces the aggregate FitnessStage3.
/// </summary>
/// <param name="BlockOrder">Shift-type rotation adherence (early -&gt; late -&gt; night)</param>
/// <param name="Blacklist">Fraction of tokens NOT on a blacklisted shift preference</param>
/// <param name="Location">Location continuity across consecutive tokens</param>
/// <param name="MaxGap">Adherence to the optimal intra-day gap between tokens</param>
public sealed record Stage3Components(
    double BlockOrder,
    double Blacklist,
    double Location,
    double MaxGap);
