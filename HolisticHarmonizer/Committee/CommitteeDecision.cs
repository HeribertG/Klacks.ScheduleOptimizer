// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Committee;

/// <param name="Approved">True when the swap may proceed to BatchEvaluator's score-greedy step. False blocks the swap as a soft veto.</param>
/// <param name="Verdicts">Verdicts of every committee member, in committee order; useful for diagnostics and reject-memory summaries.</param>
/// <param name="Summary">Short human-readable string of all veto reasons joined with semicolons; empty when Approved.</param>
public sealed record CommitteeDecision(
    bool Approved,
    IReadOnlyList<ConstraintAgentVerdict> Verdicts,
    string Summary);
