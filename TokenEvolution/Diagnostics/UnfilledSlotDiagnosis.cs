// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.TokenEvolution.Diagnostics;

/// <summary>
/// Why one (shift, date) slot of a finished plan still lacks staff.
/// </summary>
/// <param name="ShiftId">Shift the slot belongs to</param>
/// <param name="Date">Day the slot starts on</param>
/// <param name="MissingSeats">Demanded seats minus assigned seats (always at least 1)</param>
/// <param name="FeasibleAgentCount">Agents the Stage-0 hard rules would still allow on the slot against the finished plan;
/// 0 means no agent may legally take it, a positive count means the engine left it open for a softer reason</param>
/// <param name="VetoCounts">Per Stage-0 rule name the number of agents it ruled out; each agent is counted once, under the
/// first failing rule in check order</param>
public sealed record UnfilledSlotDiagnosis(
    Guid ShiftId,
    DateOnly Date,
    int MissingSeats,
    int FeasibleAgentCount,
    IReadOnlyDictionary<string, int> VetoCounts);
