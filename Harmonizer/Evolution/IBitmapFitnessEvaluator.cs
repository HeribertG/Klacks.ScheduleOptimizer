// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Harmonizer.Evolution;

/// <summary>
/// Seam that lets <see cref="HarmonizerEvolutionLoop"/> rank candidates by any plan-level objective,
/// not just the row-position-weighted harmony fitness. The default implementation is
/// <see cref="HarmonyFitnessEvaluator"/> (unchanged W2 behaviour); the W4 background optimiser injects
/// a composite-objective-backed evaluator instead. Higher Fitness is better.
/// </summary>
public interface IBitmapFitnessEvaluator
{
    FitnessResult Evaluate(HarmonyBitmap bitmap);
}
