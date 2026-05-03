// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Telemetry;

/// <param name="AgentId">Owner of the row</param>
/// <param name="RowIndex">Position of the row in conductor processing order (0 = top)</param>
/// <param name="InitialScore">Harmony score before any conductor pass</param>
/// <param name="FinalScore">Harmony score after the conductor finished</param>
/// <param name="MovesApplied">Number of accepted swap operations on the row</param>
/// <param name="EmergencyUnlockTriggered">True if the row consumed its one-shot emergency unlock</param>
public sealed record RowTelemetry(
    string AgentId,
    int RowIndex,
    double InitialScore,
    double FinalScore,
    int MovesApplied,
    bool EmergencyUnlockTriggered);
