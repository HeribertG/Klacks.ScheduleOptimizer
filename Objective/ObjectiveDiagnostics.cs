// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Objective;

/// <summary>
/// Per-dimension worst-off values the W4 runner needs for the surviving v1 no-regression guards
/// (worst-agent hours floor, per-agent blacklist cap). Carried alongside the aggregate sub-scores so
/// the runner can guard against the baseline without re-deriving them.
/// </summary>
/// <param name="WorstStundenabgleich">Minimum per-agent hours sub-score over rewarded agents (1.0 if none)</param>
/// <param name="WorstPraeferenzen">Minimum per-agent preference sub-score over agents with preferences (1.0 if none)</param>
/// <param name="MaxBlacklistFraction">Maximum per-agent blacklisted-assignment fraction (0 if none) — backs the per-agent blacklist cap guard</param>
public readonly record struct ObjectiveDiagnostics(
    double WorstStundenabgleich,
    double WorstPraeferenzen,
    double MaxBlacklistFraction);
