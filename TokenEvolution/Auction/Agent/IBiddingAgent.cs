// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Auction.Agent;

/// <summary>
/// Strategy interface for an agent's bidding behaviour.
/// Phase 1: HungerOnlyBiddingAgent. Phase 2: FuzzyBiddingAgent (Mamdani inference).
/// Implementations must be pure and stateless — all state flows through AgentRuntimeState.
/// </summary>
public interface IBiddingAgent
{
    /// <summary>
    /// Compute a bid for the given slot using the agent's contract data and current runtime state.
    /// Constraint feasibility is NOT checked here; that is the controller's job.
    /// </summary>
    Bid Evaluate(CoreAgent agent, CoreShift slot, AgentRuntimeState state, CoreWizardContext context);
}
