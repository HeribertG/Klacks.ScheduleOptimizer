// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// What the carry-in pre-pass left behind: the slot occupancy a seeding strategy must respect so it
/// does not staff a slot twice, and the tokens the pre-pass placed.
/// </summary>
/// <param name="Occupancy">Slots covered by locked and constructed tokens</param>
/// <param name="Placed">Tokens the pre-pass appended, in placement order</param>
/// <param name="Anchor">First day the run may staff</param>
/// <param name="Packages">Open packages the pre-pass tried to continue</param>

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

public sealed record CarryInSeedResult(
    SlotOccupancy Occupancy,
    IReadOnlyList<Models.CoreToken> Placed,
    DateOnly Anchor,
    IReadOnlyList<CarryInPackage> Packages);
