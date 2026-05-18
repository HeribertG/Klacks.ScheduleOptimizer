// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// M5: Reassigns one non-locked token to a different agent that is still valid for the slot.
/// Index-aware: prefers candidates with a lower position in context.Agents (top-down list order),
/// so the GA drifts towards the architecture intent that earlier agents in the roster carry more
/// load. Without this bias, constraints and path-dependence systematically push slots to the
/// bottom of the roster — observed empirically on homogeneous full-time teams. Most aggressive
/// operator; used sparingly via MutationWeights.
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
                && SlotConstraintFilter.IsValidAssignment(a, currentToken.Date, currentToken.ShiftTypeIndex, currentToken.TotalHours, context.Wizard, tokensWithoutCurrent, currentToken.StartAt, currentToken.EndAt))
            .ToList();

        if (validAgents.Count == 0)
        {
            return TokenSwapMutation.CloneScenario(context.Primary, tokens);
        }

        var newAgent = RosterPositionBias.PickWithTopBias(validAgents, a => a.Id, context.Wizard.Agents, context.Rng);
        tokens[chosen.Index] = currentToken with { AgentId = newAgent.Id };
        return TokenSwapMutation.CloneScenario(context.Primary, tokens);
    }
}
