// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Global;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Search.Region;

/// <summary>
/// An admitted rebuild of one region: it passed the rules and the per-row score gate and raises the global harmony.
/// </summary>
/// <param name="Region">The rebuilt region.</param>
/// <param name="Steps">Same-day swaps that turn the base plan into <paramref name="Bitmap"/> when applied in order.</param>
/// <param name="Bitmap">The whole plan with the region rebuilt.</param>
/// <param name="Rhythm">Rhythm matrix in step with <paramref name="Bitmap"/>.</param>
/// <param name="Fitness">Per-row harmony fitness of <paramref name="Bitmap"/>.</param>
/// <param name="Harmony">Global harmony of <paramref name="Bitmap"/>.</param>
public sealed record RegionCandidate(
    HarmonyRegion Region,
    IReadOnlyList<PlanCellSwap> Steps,
    HarmonyBitmap Bitmap,
    RhythmMatrix Rhythm,
    double Fitness,
    GlobalHarmonyScore Harmony);
