// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction.Agent;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction.Conductor;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction.Controller;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Auction;

/// <summary>
/// Phase-1 population strategy: 3-layer auction (Conductor / BiddingAgent / Controller).
/// Used as one of several population seeders alongside Greedy/Random/CoverageFirst.
/// In Phase 1 the BiddingAgent is HungerOnly; Phase 2 swaps in FuzzyBiddingAgent.
/// </summary>
public sealed class AuctionTokenStrategy : ITokenPopulationStrategy
{
    private readonly SlotAuctioneer _auctioneer;

    public AuctionTokenStrategy()
        : this(new SlotAuctioneer(
            new FuzzyBiddingAgent(),
            new Stage0HardConstraintChecker(),
            new Stage1SoftConstraintChecker()))
    {
    }

    public AuctionTokenStrategy(SlotAuctioneer auctioneer)
    {
        _auctioneer = auctioneer;
    }

    public CoreScenario BuildScenario(CoreWizardContext context, Random rng)
    {
        var outcome = _auctioneer.Run(context, rng);
        return outcome.Scenario;
    }
}
