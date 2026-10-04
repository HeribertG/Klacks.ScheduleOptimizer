// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Harmonizer.Rules;

/// <summary>
/// Number of hard planning-rule findings before and after one engine run, measured with the same evaluator the hard
/// guard uses. The guard only rejects moves that raise a rule's excess, so a run does not create new violations, but
/// it has no repair goal either: findings that remain were there before the run.
/// </summary>
/// <param name="Before">Hard findings of the plan the run started from</param>
/// <param name="After">Hard findings of the plan the run produced</param>
public sealed record PlanningRuleHardCounts(int Before, int After);
