// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

/// <summary>
/// Classification of the soft-constraint that Wizard 1's SlotAuctioneer relaxed when assigning
/// a slot in Round 2. Drives priority of the corresponding row inside the harmonizer.
/// </summary>
public enum SofteningKind : byte
{
    Unknown = 0,
    MinRestDays = 1,
    MaxConsecutiveWorkDays = 2,
    PreferredShiftViolation = 3,
    BlacklistedShiftAssigned = 4,
    HeavyWeeklyLoad = 5,
}
