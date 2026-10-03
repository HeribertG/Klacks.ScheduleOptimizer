// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

/// <summary>
/// Why a swap proposed by the LLM was discarded by the Holistic Harmonizer hard-constraint layer.
/// </summary>
public enum PlanMutationRejectionReason : byte
{
    /// <summary>One or both cells are locked (Work.LockLevel != None or Break).</summary>
    LockedCell = 0,

    /// <summary>One or both coordinates point outside the bitmap dimensions.</summary>
    OutOfBounds = 1,

    /// <summary>The swap would violate a hard scheduling constraint (caps, pause, etc.).</summary>
    HardConstraintViolation = 2,

    /// <summary>The cells already hold the same symbol — swapping is a no-op.</summary>
    NoEffect = 3,

    /// <summary>The swap was hard-valid but vetoed by a majority of constraint-agents.</summary>
    CommitteeVeto = 4,

    /// <summary>The batch would move the touched rows further away from their target hours (stage 3 must not).</summary>
    TargetHoursWorsened = 5,
}
