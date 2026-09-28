// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

/// <param name="AgentId">Owner of the softened slot</param>
/// <param name="Date">Calendar date of the softened slot</param>
/// <param name="Kind">Which soft-constraint was relaxed</param>
/// <param name="RuleNames">Names of the bidding rules that fired during the softening, for diagnostics</param>
public sealed record SofteningHint(
    string AgentId,
    DateOnly Date,
    SofteningKind Kind,
    IReadOnlyList<string> RuleNames);
