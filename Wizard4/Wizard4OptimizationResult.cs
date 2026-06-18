// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Objective;

namespace Klacks.ScheduleOptimizer.Wizard4;

/// <param name="BestBitmap">The best arrangement found (never worse than the snapshot — the seed is carried by elitism)</param>
/// <param name="Baseline">Composite result of the immutable snapshot the optimisation started from</param>
/// <param name="Best">Composite result of the best arrangement (re-scored for the candidate/diff)</param>
/// <param name="BaselineScalar">The snapshot's admissible composite scalar (loop fitness of the seed)</param>
/// <param name="BestFitness">The best individual's loop fitness; >= BaselineScalar by construction</param>
public sealed record Wizard4OptimizationResult(
    HarmonyBitmap BestBitmap,
    ObjectiveResult Baseline,
    ObjectiveResult Best,
    double BaselineScalar,
    double BestFitness);
