// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.TokenEvolution.Diagnostics;

/// <summary>
/// Why one (shift, date) slot of a finished plan still lacks staff. Every agent is counted exactly once: in
/// EligibilityVetoCounts, in PlacementVetoCounts or in PlaceableAgentCount.
/// </summary>
/// <param name="ShiftId">Shift the slot belongs to</param>
/// <param name="Date">Day the slot starts on</param>
/// <param name="MissingSeats">Demanded seats minus assigned seats (always at least 1)</param>
/// <param name="EligibleAgentCount">Agents the Stage-0 hard rules allow on the slot given only the fixed facts (locked
/// tokens, contracts, absences, keywords, qualifications, blacklist, existing and boundary works); 0 means the slot is
/// unsolvable without changing locked work or master data</param>
/// <param name="PlaceableAgentCount">Eligible agents the hard rules also allow against the finished plan (incl. their own
/// tokens there); 0 with EligibleAgentCount &gt; 0 means another plan could fill the slot</param>
/// <param name="EligibilityVetoCounts">Per rule name the agents that are not eligible, each under its first failing rule</param>
/// <param name="PlacementVetoCounts">Per rule name the eligible agents the finished plan blocks, each under its first failing rule</param>
public sealed record UnfilledSlotDiagnosis(
    Guid ShiftId,
    DateOnly Date,
    int MissingSeats,
    int EligibleAgentCount,
    int PlaceableAgentCount,
    IReadOnlyDictionary<string, int> EligibilityVetoCounts,
    IReadOnlyDictionary<string, int> PlacementVetoCounts);
