// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Telemetry;

/// <summary>
/// Per-run telemetry record capturing everything needed to empirically tune the
/// emergency-unlock threshold via the autoresearch pipeline. Persisted by the configured
/// IHarmonizerTelemetrySink at the end of every harmonizer run.
/// </summary>
/// <param name="JobId">Unique job identifier</param>
/// <param name="PeriodFrom">Inclusive start of the harmonised period</param>
/// <param name="PeriodUntil">Inclusive end of the harmonised period</param>
/// <param name="RowCount">Number of agent rows in the bitmap</param>
/// <param name="InitialFitness">Weighted average row score before harmonisation</param>
/// <param name="FinalFitness">Weighted average row score after harmonisation</param>
/// <param name="EmergencyThreshold">Threshold value (fraction of median) used in this run</param>
/// <param name="GenerationsRun">Number of GA generations actually executed</param>
/// <param name="TotalEmergencyUnlocks">Sum of rows that triggered an emergency unlock</param>
/// <param name="DurationMs">Wall-clock duration of the run in milliseconds</param>
/// <param name="Rows">Per-row telemetry, indexed by RowIndex</param>
public sealed record HarmonizerRunTelemetry(
    Guid JobId,
    DateOnly PeriodFrom,
    DateOnly PeriodUntil,
    int RowCount,
    double InitialFitness,
    double FinalFitness,
    double EmergencyThreshold,
    int GenerationsRun,
    int TotalEmergencyUnlocks,
    long DurationMs,
    IReadOnlyList<RowTelemetry> Rows)
{
    public double FitnessDelta => FinalFitness - InitialFitness;

    public bool IsImprovement => FitnessDelta > 0;
}
