// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// An agent as the planning-rule evaluator sees it: identity, night window and workload. No display name,
/// so findings can never carry personal data beyond the id.
/// </summary>
/// <param name="Id">Agent id (Client.Id)</param>
/// <param name="NightWindow">Effective contractual night window; null falls back to ShiftTypeIndex 2</param>
/// <param name="WorkloadPercent">Employment level in percent, used by pro-rata fairness</param>

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public sealed record RuleAgent(string Id, CoreNightWindow? NightWindow, decimal WorkloadPercent);
