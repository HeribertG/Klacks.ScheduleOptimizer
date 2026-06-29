// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Evolution;

/// <param name="MaxIterations">Hard upper bound on annealing steps; each step is one proposal</param>
/// <param name="InitialTemperature">Starting Metropolis temperature; used directly when TargetInitialAcceptance is null, otherwise overridden by acceptance-ratio calibration</param>
/// <param name="CoolingRate">Geometric cooling factor applied after every step (0 &lt; rate &lt; 1)</param>
/// <param name="MovesPerStep">Number of random same-day swaps applied per proposal before the optional conductor pass</param>
/// <param name="RunConductorPerStep">When true each proposal is locally optimised by a conductor pass (memetic annealing); when false the chain is pure Metropolis sampling over stochastic moves</param>
/// <param name="TargetInitialAcceptance">Desired initial acceptance probability for worsening moves; when set (0 &lt; p &lt; 1) the initial temperature is calibrated to it, null disables calibration and uses InitialTemperature</param>
/// <param name="Seed">Deterministic seed for the random source; null for non-deterministic runs</param>
/// <param name="MaxRuntime">Optional soft wall-clock budget; when exceeded the loop stops gracefully and returns the best individual found so far. Null = no time limit</param>
public sealed record HarmonizerAnnealingConfig(
    int MaxIterations,
    double InitialTemperature = 0.1,
    double CoolingRate = 0.95,
    int MovesPerStep = 2,
    bool RunConductorPerStep = true,
    double? TargetInitialAcceptance = 0.8,
    int? Seed = null,
    TimeSpan? MaxRuntime = null);
