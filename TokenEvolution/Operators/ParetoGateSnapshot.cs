// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// The numbered-rule reading of one plan, reduced to the values a Pareto comparison needs.
/// </summary>
/// <param name="Legality">Stage-0 legality violation count (rules 2 and 3); lower is better</param>
/// <param name="Stage0">Hard-constraint violation count (rule 1); lower is better</param>
/// <param name="Stage1">GuaranteedHours coverage top-down (rule 5); higher is better</param>
/// <param name="Stage2">FullTime coverage top-down (rule 5); higher is better</param>
/// <param name="BlockOrder">Rotation and package constancy (rules 7 and 8); higher is better</param>
/// <param name="Blacklist">Share of tokens off a blacklisted shift (owner decision B2); higher is better</param>
/// <param name="ShiftKindFairness">Evenness of the shift kinds over the eligible agents (rule 9); higher is better</param>
/// <param name="OverlongPackages">Packages longer than the agent's MaxWorkDays (rule 6); lower is better</param>
public readonly record struct ParetoGateSnapshot(
    int Legality,
    int Stage0,
    double Stage1,
    double Stage2,
    double BlockOrder,
    double Blacklist,
    double ShiftKindFairness,
    int OverlongPackages);
