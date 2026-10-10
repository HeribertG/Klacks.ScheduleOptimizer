// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Common.Rotation;

/// <summary>
/// How well one more shift fits an agent's rotation (<see cref="ShiftRotation.Fit"/>), best first. A non-ideal block
/// change ranks below a change inside a block: rotation before purity (SPEC-ROTATION-2026-10-08 rule 4).
/// </summary>
public enum RotationFit
{
    Conform = 0,
    InBlockChange = 1,
    NonIdealTransition = 2,
}
