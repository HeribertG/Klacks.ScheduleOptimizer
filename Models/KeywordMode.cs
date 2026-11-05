// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Models;

/// <summary>
/// How a run treats the restricting schedule commands (FREE, OnlyX, NoX). Hard vetoes them like any other hard rule.
/// Soft turns them into ranked penalties (FREE above OnlyX above NoX) that rank below the guaranteed hours and above
/// the target hours, so a slot can still be covered against a wish. Positive wishes (-FREE, OnlyX) attract in both modes.
/// </summary>
public enum KeywordMode
{
    Hard,
    Soft,
}
