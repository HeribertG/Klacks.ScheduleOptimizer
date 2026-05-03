// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Conductor;

namespace Klacks.ScheduleOptimizer.Harmonizer.Evolution;

/// <param name="Bitmap">The genome — a candidate harmonisation of the input bitmap</param>
/// <param name="Fitness">Weighted average row score; higher is better</param>
/// <param name="RowScores">Per-row scores at the time of evaluation</param>
/// <param name="ConductorTrace">Result of the conductor pass that produced this individual; carries per-row before/after scores and emergency-unlock flags for telemetry</param>
public sealed record Individual(
    HarmonyBitmap Bitmap,
    double Fitness,
    IReadOnlyList<double> RowScores,
    ConductorResult ConductorTrace);
