// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

/// <summary>Discriminator of the planning-rule families evaluated by the shared rule evaluator.</summary>
public enum PlanRuleKind
{
    MaxConsecutiveOfKind,
    ForbiddenTransition,
    RestAfterKind,
    PeriodCount,
    TeamFairness,
}
