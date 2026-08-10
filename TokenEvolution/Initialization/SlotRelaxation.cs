// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Rungs of the coverage escalation, ordered from the strictest to the widest candidate set. Every
/// rung admits everything the rung before it admits, so climbing the ladder can only add candidates.
/// Hard rules — qualification, bans, keywords, breaks, contract days, hour caps, minimum pause,
/// restricted windows, physical collisions and the MaxConsecutiveDays cap — hold on every rung.
/// </summary>
public enum SlotRelaxation
{
    /// <summary>Every rule applies, including MinRestDays and the MaxWorkDays block ideal.</summary>
    None = 0,

    /// <summary>The free block between two packages steps aside; the block ideal still holds.</summary>
    RestDaysOnly = 1,

    /// <summary>Last resort: the MaxWorkDays block ideal steps aside as well, hard rules do not.</summary>
    All = 2,
}
