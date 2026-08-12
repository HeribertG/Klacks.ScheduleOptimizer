// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Rungs of the coverage escalation, ordered from the strictest to the widest candidate set. Every
/// rung admits everything the rung before it admits, so climbing the ladder can only add candidates.
/// Hard rules — qualification, bans, keywords, breaks, contract days, hour caps, minimum pause,
/// restricted windows, physical collisions and the MaxConsecutiveDays cap — hold on every rung.
/// Since the owner ruling of 2026-08-12 (SPEC.md decision 12d) the package rest — MinRestDays
/// computed as hours between shift end and shift start — also holds on every rung: coverage may no
/// longer buy a fill by shortening the rest between two packages.
/// </summary>
public enum SlotRelaxation
{
    /// <summary>Every rule applies, including the MaxWorkDays block ideal.</summary>
    None = 0,

    /// <summary>
    /// Historic rung that used to let the rest between two packages step aside; since the 2026-08-12
    /// ruling it admits exactly what <see cref="None"/> admits and remains only as the intermediate
    /// step of the ladder (and as the receiver level of relocations).
    /// </summary>
    RestDaysOnly = 1,

    /// <summary>Last resort: the MaxWorkDays block ideal steps aside, hard rules and rest do not.</summary>
    All = 2,
}
