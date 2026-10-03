// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// An agent as the planning-rule evaluator sees it: identity, night window and workload. No display name,
/// so findings can never carry personal data beyond the id.
/// </summary>
/// <param name="Id">Agent id (Client.Id)</param>
/// <param name="NightWindow">Effective contractual night window; null falls back to ShiftTypeIndex 2</param>
/// <param name="WorkloadPercent">Employment level in percent, used by pro-rata fairness</param>
/// <param name="NightRuleMinOverlapMinutes">Night overlap a segment must EXCEED to make its day a Night day for the
/// sequence and fairness rules (company setting NIGHT_RULE_MIN_OVERLAP_MINUTES, owner decision 7); 0 = from the
/// first minute. PeriodCount night counting ignores it and always counts any overlap (CounterRule parity).</param>

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public sealed record RuleAgent(string Id, CoreNightWindow? NightWindow, decimal WorkloadPercent, int NightRuleMinOverlapMinutes = 0);
