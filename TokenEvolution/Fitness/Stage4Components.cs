// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

/// <summary>
/// Per-dimension breakdown of the Stage-4 cosmetic score, each value in [0..1] where 1 is best.
/// The aggregate FitnessStage4 is their unweighted mean.
/// </summary>
/// <param name="Fairness">Evenness of the target-hours coverage ratio across agents</param>
/// <param name="MinimumHours">Weighted coverage of each agent's contractual minimum hours</param>
/// <param name="BlockSymmetry">Uniformity of consecutive-day block lengths</param>
public sealed record Stage4Components(
    double Fairness,
    double MinimumHours,
    double BlockSymmetry);
