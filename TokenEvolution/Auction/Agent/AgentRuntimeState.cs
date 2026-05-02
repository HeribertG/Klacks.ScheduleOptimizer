// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution.Auction.Agent;

/// <summary>
/// Per-agent transient state used by the bidding agent to compute scores.
/// Lives only within a single auction run; never persisted between GA generations.
/// </summary>
/// <param name="AgentId">Agent identifier</param>
/// <param name="HoursAssignedThisRun">Hours the auction has assigned so far</param>
/// <param name="CurrentBlockLength">Length of the in-progress consecutive work block (0 if last day was rest)</param>
/// <param name="LastWorkedDate">Most recent assigned date or null</param>
/// <param name="DaysSinceShiftType">Index 0=early, 1=late, 2=night → days since last shift of that type (int.MaxValue if never)</param>
public sealed record AgentRuntimeState(
    string AgentId,
    double HoursAssignedThisRun,
    int CurrentBlockLength,
    DateOnly? LastWorkedDate,
    IReadOnlyList<int> DaysSinceShiftType)
{
    public static AgentRuntimeState Initial(string agentId) =>
        new(agentId, 0.0, 0, null, [int.MaxValue, int.MaxValue, int.MaxValue]);
}
