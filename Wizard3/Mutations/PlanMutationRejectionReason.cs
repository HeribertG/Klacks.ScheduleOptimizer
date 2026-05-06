// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Wizard3.Mutations;

/// <summary>
/// Why a swap proposed by the LLM was discarded by the Wizard 3 hard-constraint layer.
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
}
