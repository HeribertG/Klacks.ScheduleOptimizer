// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Committee;

/// <param name="AgentName">Stable identifier of the agent that produced the verdict (e.g. "Hours", "Pause").</param>
/// <param name="Vote">Approve, Veto, or Abstain.</param>
/// <param name="Reason">Human-readable justification surfaced in reject memory and prompts; null for abstentions.</param>
public sealed record ConstraintAgentVerdict(string AgentName, ConstraintAgentVote Vote, string? Reason);
