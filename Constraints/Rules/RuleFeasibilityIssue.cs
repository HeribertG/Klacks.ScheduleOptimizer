// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// A shift kind whose demand the hard sequence rules make unreachable even under the most favourable plan,
/// while the available agent-days alone would cover it. Ids only, no names.
/// </summary>
/// <param name="Kind">Shift kind of the unreachable demand</param>
/// <param name="RuleIds">Hard rules that limit this kind</param>
/// <param name="Demand">Required agent-days of the kind in the period</param>
/// <param name="Capacity">Upper bound of agent-days of the kind the rules allow</param>
/// <param name="AvailableDays">Agent-days available without the rules</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public sealed record RuleFeasibilityIssue(
    RuleShiftKind Kind,
    IReadOnlyList<Guid> RuleIds,
    int Demand,
    int Capacity,
    int AvailableDays);
