// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Harmonizer.Rules;

/// <summary>
/// Number of hard planning-rule findings before and after one engine run, measured with the same evaluator the hard
/// guard uses. The guard only rejects moves that raise an agent's summed excess of a hard rule, so no hard rule gets
/// worse in total; the run has no repair goal, and a finding can shift or split within a rule, so the count is a
/// summary, not a per-finding identity.
/// </summary>
/// <param name="Before">Hard findings of the plan the run started from</param>
/// <param name="After">Hard findings of the plan the run produced</param>
public sealed record PlanningRuleHardCounts(int Before, int After);
