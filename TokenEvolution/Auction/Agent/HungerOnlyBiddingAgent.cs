// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Auction.Agent;

/// <summary>
/// Phase-1 placeholder bidding agent: bids in proportion to remaining guaranteed-hours deficit.
/// Will be replaced by FuzzyBiddingAgent in Phase 2. Output is intentionally simple so the
/// auction skeleton can be tested end-to-end without depending on a fuzzy engine.
/// </summary>
public sealed class HungerOnlyBiddingAgent : IBiddingAgent
{
    private const string DummyRuleName = "P1_HungerOnly";

    public Bid Evaluate(CoreAgent agent, CoreShift slot, AgentRuntimeState state, CoreWizardContext context)
    {
        var target = ResolveTarget(agent);
        if (double.IsPositiveInfinity(target))
        {
            return new Bid(agent.Id, 0.5, [DummyRuleName]);
        }

        var totalAssigned = agent.CurrentHours + state.HoursAssignedThisRun;
        var deficit = target - totalAssigned;
        if (deficit <= 0)
        {
            return Bid.NoBid(agent.Id);
        }

        var score = Math.Clamp(deficit / target, 0.0, 1.0);
        return new Bid(agent.Id, score, [DummyRuleName]);
    }

    private static double ResolveTarget(CoreAgent agent)
    {
        if (agent.GuaranteedHours > 0)
        {
            return agent.GuaranteedHours;
        }

        return agent.FullTime > 0 ? agent.FullTime : double.PositiveInfinity;
    }
}
