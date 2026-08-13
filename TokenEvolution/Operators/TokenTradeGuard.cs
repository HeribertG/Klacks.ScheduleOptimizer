// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction.Controller;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// Shared mechanics of an agent trade between two tokens: building the traded pair with the
/// surcharges of each NEW holder (copying them along would falsify the stage-1 hours account) and
/// re-checking both traded tokens against stage 0 and the slot filter. Extracted verbatim from
/// TokenSwapMutation so the targeted package-consolidation trade validates identically.
/// </summary>
internal static class TokenTradeGuard
{
    /// <summary>Both tokens with exchanged agents and the surcharges of their new holders.</summary>
    internal static (CoreToken First, CoreToken Second) TradeAgents(
        CoreToken a, CoreToken b, IReadOnlyDictionary<string, CoreAgent> agentsById)
    {
        var swappedA = a with
        {
            AgentId = b.AgentId,
            Surcharges = SurchargeEstimator.Estimate(
                a.TotalHours, a.ShiftTypeIndex, a.Date, agentsById.GetValueOrDefault(b.AgentId)),
        };
        var swappedB = b with
        {
            AgentId = a.AgentId,
            Surcharges = SurchargeEstimator.Estimate(
                b.TotalHours, b.ShiftTypeIndex, b.Date, agentsById.GetValueOrDefault(a.AgentId)),
        };
        return (swappedA, swappedB);
    }

    /// <summary>
    /// True when either traded token violates stage 0 or the slot filter in the plan that already
    /// contains both traded tokens.
    /// </summary>
    internal static bool RejectsTrade(
        Stage0HardConstraintChecker stage0,
        CoreToken first,
        CoreToken second,
        IReadOnlyList<CoreToken> allTokens,
        IReadOnlyDictionary<string, CoreAgent> agentsById,
        CoreWizardContext wizard)
    {
        var othersForFirst = new List<CoreToken>(allTokens.Count - 1);
        var othersForSecond = new List<CoreToken>(allTokens.Count - 1);
        for (var i = 0; i < allTokens.Count; i++)
        {
            var t = allTokens[i];
            if (!ReferenceEquals(t, first))
            {
                othersForFirst.Add(t);
            }
            if (!ReferenceEquals(t, second))
            {
                othersForSecond.Add(t);
            }
        }

        return IsRejected(stage0, first, othersForFirst, agentsById, wizard)
            || IsRejected(stage0, second, othersForSecond, agentsById, wizard);
    }

    private static bool IsRejected(
        Stage0HardConstraintChecker stage0,
        CoreToken token,
        IReadOnlyList<CoreToken> others,
        IReadOnlyDictionary<string, CoreAgent> agentsById,
        CoreWizardContext wizard)
    {
        if (stage0.ValidateToken(token, others, wizard) != null)
        {
            return true;
        }

        if (!agentsById.TryGetValue(token.AgentId, out var agent))
        {
            return true;
        }

        return !SlotConstraintFilter.IsValidAssignment(
            agent,
            token.Date,
            token.ShiftTypeIndex,
            token.ShiftRefId,
            token.TotalHours,
            wizard,
            others,
            token.StartAt,
            token.EndAt);
    }
}
