// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Diagnostics;

/// <summary>
/// Counts the qualification levels a plan's tokens are missing below NON-binding minimum levels
/// (<see cref="CoreWizardContext.LevelShortfall"/>). One token counts at most <see cref="MaxLevelGapPerToken"/> levels:
/// the context sums the gaps of every requirement of a slot, so a slot with several too-low qualifications could
/// otherwise outweigh the worst case the fitness term normalises by. Shared by the Stage-3 fitness term and the
/// Pareto fairness gate so both read the same number.
/// </summary>
public static class LevelShortfallTrace
{
    /// <summary>Largest level gap one token counts (Expert 5 minus Low 1); the normaliser of the fitness term.</summary>
    public const int MaxLevelGapPerToken = 4;

    /// <summary>Missing levels over all tokens of the plan, each token capped at <see cref="MaxLevelGapPerToken"/>.</summary>
    /// <param name="scenario">Plan to measure</param>
    /// <param name="context">Wizard context carrying the per-assignment level shortfall</param>
    public static int MissingLevels(CoreScenario scenario, CoreWizardContext context)
    {
        if (context.LevelShortfall.Count == 0)
        {
            return 0;
        }

        var missing = 0;
        foreach (var token in scenario.Tokens)
        {
            missing += Math.Min(MaxLevelGapPerToken, context.LevelShortfallOf(token.AgentId, token.ShiftRefId, token.Date));
        }

        return missing;
    }
}
