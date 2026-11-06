// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Models;

/// <summary>
/// How a run treats the restricting schedule commands (FREE, OnlyX, NoX). Hard vetoes them like any other hard rule.
/// Soft turns them into wishes in strict tiers (one broken FREE outweighs any number of broken OnlyX, one OnlyX any number
/// of NoX) that rank right after coverage and before the guaranteed hours: a wish is broken only to cover an otherwise
/// open slot, never for guaranteed or target hours. Positive wishes (-FREE, OnlyX) attract in both modes.
/// </summary>
public enum KeywordMode
{
    Hard,
    Soft,
}
