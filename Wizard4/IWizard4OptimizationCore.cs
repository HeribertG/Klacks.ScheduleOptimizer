// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Conductor;
using Klacks.ScheduleOptimizer.Harmonizer.Evolution;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Wizard4;

/// <summary>
/// Seam over <see cref="Wizard4OptimizationCore"/> so the Api-side Wizard4 runner can be unit-tested
/// against a stubbed engine (the real engine needs a full bitmap + context and is exercised by the
/// optimizer's own tests).
/// </summary>
public interface IWizard4OptimizationCore
{
    Wizard4OptimizationResult Optimize(
        HarmonyBitmap seed,
        CoreWizardContext objectiveContext,
        DomainAwareReplaceValidator validator,
        HarmonizerEvolutionConfig config,
        IReadOnlyList<SofteningHint>? hints = null,
        IProgress<EvolutionGenerationProgress>? progress = null,
        bool enforceCoverageFloor = true,
        CancellationToken ct = default);
}
