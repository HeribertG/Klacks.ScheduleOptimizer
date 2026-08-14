// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Verdict;

/// <summary>
/// One concrete rest gap of one agent that fell short: either the gap between two work packages
/// missing the agent's configured package rest, or a day-to-day turnaround inside a package
/// missing the agent's daily rest. A scratch stays above the legal minimum and only lowers the
/// score; a legal-minimum or daily-rest breach caps the whole verdict regardless of any quota.
/// </summary>
/// <param name="AgentId">Agent whose rest gap fell short</param>
/// <param name="GapStart">Moment the earlier package or working day ended</param>
/// <param name="GapEnd">Moment the later package or working day started</param>
/// <param name="GapHours">Measured free hours between the two</param>
/// <param name="RequiredHours">Hours the shorted rest rule demands</param>
/// <param name="Kind">Scratch, legal-minimum breach, or daily-rest breach</param>
public sealed record VerdictFinding(
    string AgentId,
    DateTime GapStart,
    DateTime GapEnd,
    double GapHours,
    double RequiredHours,
    VerdictFindingKind Kind);
