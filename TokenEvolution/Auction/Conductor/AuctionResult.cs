// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.TokenEvolution.Auction.Agent;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction.Controller;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Auction.Conductor;

/// <summary>
/// Outcome of a single slot auction.
/// Round 1 = clean win (no Stage-1 violations), Round 2 = win with escalation, Round 3 = unfilled.
/// </summary>
/// <param name="SlotId">Slot identifier (date + shift ref)</param>
/// <param name="WinnerAgentId">Winning agent or null if Round = 3</param>
/// <param name="Round">1, 2, or 3</param>
/// <param name="Bids">All bids received (for telemetry)</param>
/// <param name="Vetos">All vetos issued (for telemetry)</param>
public sealed record AuctionResult(
    string SlotId,
    string? WinnerAgentId,
    int Round,
    IReadOnlyList<Bid> Bids,
    IReadOnlyList<VetoVerdict> Vetos);
