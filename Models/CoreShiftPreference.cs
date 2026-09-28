// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Master-data preference of an agent for a particular shift definition.
/// Soft-constraint: Preferred adds to greed, Blacklist adds to disgust in the motivation formula.
/// </summary>
/// <param name="AgentId">The agent</param>
/// <param name="ShiftRefId">The shift definition</param>
/// <param name="Kind">Preference kind (Preferred or Blacklist)</param>

namespace Klacks.ScheduleOptimizer.Models;

public record CoreShiftPreference(
    string AgentId,
    Guid ShiftRefId,
    ShiftPreferenceKind Kind);
