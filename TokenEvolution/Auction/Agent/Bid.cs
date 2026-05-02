// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution.Auction.Agent;

/// <summary>
/// A bid an agent submits for a slot during an auction round.
/// Score is in [0..1] where 0 = "do not want", 1 = "must have".
/// FiredRules contains the names of the fuzzy rules that produced this score (for explainability);
/// in Phase 1 this is a stub list with the dummy rule name.
/// </summary>
/// <param name="AgentId">The bidding agent's id</param>
/// <param name="Score">Annahme-Score in [0..1]</param>
/// <param name="FiredRules">Names of rules that contributed to the score</param>
public sealed record Bid(string AgentId, double Score, IReadOnlyList<string> FiredRules)
{
    public static Bid NoBid(string agentId) =>
        new(agentId, 0.0, Array.Empty<string>());
}
