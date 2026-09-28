// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Committee;

/// <summary>
/// Vote cast by a single deterministic constraint agent on a proposed swap. Approve/Veto/Abstain
/// follow the convention: Approve = "this swap helps in my dimension", Veto = "this swap hurts",
/// Abstain = "my dimension is unaffected or indifferent". The committee aggregates by simple
/// majority of non-abstain votes — a tie or all-abstain results in approval.
/// </summary>
public enum ConstraintAgentVote : byte
{
    Approve = 0,
    Veto = 1,
    Abstain = 2,
}
