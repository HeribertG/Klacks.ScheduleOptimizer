// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Common.Rotation;

/// <summary>
/// One worked calendar day of one agent as the rotation rule sees it. Two shifts on one day are a permitted split
/// duty, not a rotation step, so a day carries the kind of its earliest and of its latest shift.
/// </summary>
/// <param name="Date">Calendar day</param>
/// <param name="FirstKindIndex">Shift kind (0 early, 1 late, 2 night) of the earliest shift of the day</param>
/// <param name="LastKindIndex">Shift kind of the latest shift of the day</param>
/// <param name="FirstStart">Start of the earliest shift</param>
/// <param name="LastEnd">End of the latest shift</param>
public readonly record struct RotationDay(
    DateOnly Date,
    int FirstKindIndex,
    int LastKindIndex,
    DateTime FirstStart,
    DateTime LastEnd);
