// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// M5: Reassigns one non-locked token to a different agent that is still valid for the slot.
/// Index-aware (top-down roster rule): receivers still below their guaranteed hours are
/// preferred top-first so the top of the roster reaches its target; once every valid receiver
/// is at or above target the token drifts to the bottom of the roster, keeping the top
/// accurate ("the bottom eats what is left"). Most aggressive operator; used sparingly via
/// MutationWeights.
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

        var newAgent = RosterPositionBias.PickAccuracyAware(validAgents, tokensWithoutCurrent, context.Wizard.Agents, context.Rng);
        tokens[chosen.Index] = currentToken with { AgentId = newAgent.Id };
        return TokenSwapMutation.CloneScenario(context.Primary, tokens);
    }
}
