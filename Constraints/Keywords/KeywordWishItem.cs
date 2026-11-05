// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Constraints.Keywords;

/// <summary>
/// One missed wish of the plan.
/// </summary>
/// <param name="AgentId">The employee.</param>
/// <param name="Date">The day.</param>
/// <param name="Keyword">Combined keyword of a broken restriction, or the positive wish that went unfulfilled.</param>
/// <param name="Outcome">How the wish was missed.</param>
public sealed record KeywordWishItem(string AgentId, DateOnly Date, ScheduleCommandKeyword Keyword, KeywordWishOutcome Outcome);
