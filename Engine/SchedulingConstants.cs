// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Constants for the scheduling algorithm, mirroring automation-constants.ts.
/// These are the tunable parameters that the autoresearch loop can optimize.
/// </summary>

namespace Klacks.ScheduleOptimizer.Engine;

public static class SchedulingConstants
{
    public const double SUCCESS_COVERAGE_THRESHOLD = 0.8;
    public const double RANDOM_ASSIGNMENT_PROBABILITY = 0.7;
    public const double OVERTIME_THRESHOLD_FACTOR = 1.2;
    public const double LOW_MOTIVATION_THRESHOLD = 0.2;
    public const double FAIRNESS_MAX_DEVIATION_RATIO = 0.5;
    public const int TOURNAMENT_SIZE = 3;
}

public static class EvolutionConstants
{
    public const double MUTATION_SWAP_THRESHOLD = 0.25;
    public const double MUTATION_REMOVE_THRESHOLD = 0.40;
    public const double MUTATION_REPAIR_THRESHOLD = 0.70;
    public const double GREEDY_SHUFFLE_FACTOR = 0.3;
    public const double CROSSOVER_SWAP_PROBABILITY = 0.5;
    public const double GREEDY_HOUR_DEFICIT_WEIGHT = 2;
    public const double GREEDY_MOTIVATION_WEIGHT = 10;
    public const int PROGRESS_REPORT_INTERVAL = 5;
    public const int CONVERGENCE_HISTORY_SIZE = 10;
    public const double MS_PER_HOUR = 1000.0 * 60 * 60;
    public const double MS_PER_DAY = 1000.0 * 60 * 60 * 24;
    public const uint RNG_MULTIPLIER = 1664525;
    public const uint RNG_INCREMENT = 1013904223;
    public const uint RNG_MODULUS = 0; // 2^32 via uint overflow
    public const int ID_RANDOM_RANGE = 10000;
    public const double GREEDY_BLOCK_CONSISTENCY_WEIGHT = 5;
}

public static class AgentStateConstants
{
    public const double DEFAULT_SATISFACTION = 0.5;
    public const double DEFAULT_MOTIVATION = 0.5;
}
