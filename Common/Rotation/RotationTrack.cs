// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Common.Rotation;

/// <summary>
/// An agent's rotation position while a plan is built shift by shift in date order.
/// </summary>
/// <param name="BlockStartKind">Kind the agent's current block started with</param>
/// <param name="Last">The agent's latest worked day</param>
public readonly record struct RotationTrack(int BlockStartKind, RotationDay Last);
