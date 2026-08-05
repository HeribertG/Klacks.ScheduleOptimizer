// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Scoring;

/// <summary>
/// Single source of truth for the version of the SubScoreJson blob shared by all engine serialisers.
/// Both <c>EngineScoreSerializer</c> and <c>ScenarioScoreSerializer</c> write into the same
/// AnalyseScenario.SubScoreJson column, so their schema version must never drift apart.
/// </summary>
public static class SubScoreSchema
{
    public const int Version = 1;
}
