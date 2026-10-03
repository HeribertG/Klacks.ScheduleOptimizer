// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// How many agents a plan needs per day for one shift kind, as input of the pre-flight feasibility check.
/// </summary>
/// <param name="Kind">Shift kind the slots are of (Night, Early, Late or Work for any slot)</param>
/// <param name="SlotsPerDay">Required agents per day index of the evaluation context</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public sealed record RuleFeasibilityDemand(RuleShiftKind Kind, IReadOnlyList<int> SlotsPerDay);
