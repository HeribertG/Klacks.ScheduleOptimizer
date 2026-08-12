// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// M5: Reassigns one non-locked token to a different agent that is still valid for the slot.
/// Index-aware (top-down roster rule): receivers still below their guaranteed hours are
/// preferred top-first so the top of the roster reaches its target; once every valid receiver
/// is at or above target the token drifts to the bottom of the roster, keeping the top
/// accurate ("the bottom eats what is left"). Package-aware since SPEC.md decision 13: inside
/// the accuracy group a receiver whose own shift starts on a neighbouring day is preferred, so
/// the reassigned token extends one of their packages instead of opening a new one-day block —
/// the mutation samples package-friendly neighbours and the fitness comparison still decides.
/// Most aggressive operator; used sparingly via MutationWeights.
/// </summary>
public sealed class ReassignMutation : ITokenOperator
{
    public CoreScenario Apply(TokenOperatorContext context)
    {
        var tokens = context.Primary.Tokens.ToList();
        var candidates = tokens
            .Select((t, idx) => (Token: t, Index: idx))
            .Where(x => !x.Token.IsLocked)
            .ToList();

        if (candidates.Count == 0)
        {
            return TokenSwapMutation.CloneScenario(context.Primary, tokens);
        }

        var chosen = candidates[context.Rng.Next(candidates.Count)];
        var currentToken = chosen.Token;
        var tokensWithoutCurrent = tokens.Where((_, i) => i != chosen.Index).ToList();
        var validAgents = context.Wizard.Agents
            .Where(a => a.Id != currentToken.AgentId
                && SlotConstraintFilter.IsValidAssignment(a, currentToken.Date, currentToken.ShiftTypeIndex, currentToken.ShiftRefId, currentToken.TotalHours, context.Wizard, tokensWithoutCurrent, currentToken.StartAt, currentToken.EndAt))
            .ToList();

        if (validAgents.Count == 0)
        {
            return TokenSwapMutation.CloneScenario(context.Primary, tokens);
        }

        var newAgent = RosterPositionBias.PickAccuracyAware(
            validAgents,
            tokensWithoutCurrent,
            context.Wizard.Agents,
            context.Rng,
            agent => SlotConstraintFilter.StartsOnDate(
                    agent.Id, currentToken.Date.AddDays(-1), tokensWithoutCurrent, context.Wizard)
                || SlotConstraintFilter.StartsOnDate(
                    agent.Id, currentToken.Date.AddDays(+1), tokensWithoutCurrent, context.Wizard));
        // Without re-estimating, the token would carry the PREVIOUS agent's night and weekend rates
        // into the new agent's hours account.
        tokens[chosen.Index] = currentToken with
        {
            AgentId = newAgent.Id,
            Surcharges = SurchargeEstimator.Estimate(
                currentToken.TotalHours, currentToken.ShiftTypeIndex, currentToken.Date, newAgent),
        };
        return TokenSwapMutation.CloneScenario(context.Primary, tokens);
    }
}
