// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

/// <param name="Id">Agent identifier (matches Klacks Client.Id)</param>
/// <param name="DisplayName">Human-readable name for diagnostics</param>
/// <param name="TargetHours">Soll-Stunden over the bitmap date range</param>
/// <param name="PreferredShiftSymbols">Shift symbols the agent prefers, derived from ClientShiftPreference (Preferred entries)</param>
/// <param name="MaxWeeklyHours">Maximum weekly hours from the active contract; 0 = unconstrained</param>
/// <param name="MaxConsecutiveDays">Maximum consecutive working days from the active contract; 0 = unconstrained</param>
/// <param name="MinPauseHours">Minimum rest hours between two work spans from the active contract; 0 = unconstrained</param>
/// <param name="BlacklistedShiftIds">Set of Shift ids the agent must not be assigned to (ClientShiftPreference Blacklist)</param>
/// <param name="MinRestDays">Minimum rest days per calendar week, rounded up; 0 = unconstrained</param>
/// <param name="NightWindow">Effective contractual night window for planning rules; null = fall back to the Night symbol</param>
/// <param name="WorkloadPercent">Employment level in percent for pro-rata planning rules; null = full time</param>
public sealed record BitmapAgent(
    string Id,
    string DisplayName,
    decimal TargetHours,
    IReadOnlySet<CellSymbol> PreferredShiftSymbols,
    decimal MaxWeeklyHours = 0m,
    int MaxConsecutiveDays = 0,
    decimal MinPauseHours = 0m,
    IReadOnlySet<Guid>? BlacklistedShiftIds = null,
    int MinRestDays = 0,
    CoreNightWindow? NightWindow = null,
    decimal? WorkloadPercent = null);
