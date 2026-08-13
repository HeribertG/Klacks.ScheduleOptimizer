// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Verdict;

/// <summary>
/// One concrete rest gap between two work packages of one agent that fell short of the agent's
/// configured package rest. A scratch stays above the legal minimum and only lowers the score;
/// a legal-minimum breach caps the whole verdict regardless of any quota.
/// </summary>
/// <param name="AgentId">Agent whose rest gap fell short</param>
/// <param name="GapStart">Moment the earlier package ended</param>
/// <param name="GapEnd">Moment the later package started</param>
/// <param name="GapHours">Measured free hours between the two packages</param>
/// <param name="RequiredHours">Hours the agent's configured package rest demands</param>
/// <param name="Kind">Scratch (above the legal minimum) or legal-minimum breach</param>
public sealed record VerdictFinding(
    string AgentId,
    DateTime GapStart,
    DateTime GapEnd,
    double GapHours,
    double RequiredHours,
    VerdictFindingKind Kind);
