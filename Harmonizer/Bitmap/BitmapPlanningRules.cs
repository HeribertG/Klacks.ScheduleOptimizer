// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Constraints.Rules;

namespace Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

/// <summary>
/// Planning rules (CounterRule and PlanningConstraint, unified by the API loader) a bitmap engine run must respect.
/// Hard rules veto moves that create or worsen a hard finding, soft rules lower the fitness. An empty rule list
/// switches every rule hook off, so the engines stay byte-identical without rules.
/// </summary>
/// <param name="Rules">Approved rules with their agent scopes resolved</param>
/// <param name="CarryIn">Worked segments outside the engine boundary window (rest of counted weeks/months/years)</param>
/// <param name="NightRuleMinOverlapMinutes">Company setting NIGHT_RULE_MIN_OVERLAP_MINUTES, the same value the
/// API validators classify night with (required, there is no engine default)</param>
/// <param name="IgnoredWorkIds">Work ids the rule evaluation skips (container sub-works, which the API rule readers
/// never count); null = none</param>
/// <param name="InvalidHardRuleIds">Approved hard constraints the loader skipped because their stored parameters are
/// invalid; the run plans without them and reports them as a warning instead of failing; null = none</param>
public sealed record BitmapPlanningRules(
    IReadOnlyList<PlanRule> Rules,
    IReadOnlyList<RuleSegment> CarryIn,
    int NightRuleMinOverlapMinutes,
    IReadOnlySet<Guid>? IgnoredWorkIds = null,
    IReadOnlyList<Guid>? InvalidHardRuleIds = null)
{
    public bool IsEmpty => Rules.Count == 0;
}
