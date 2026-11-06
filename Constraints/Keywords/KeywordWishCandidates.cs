// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Constraints.Keywords;

/// <summary>
/// Candidate choice of the Soft placement paths (auction round 2, the keyword rung of the repair, the forced coverage of
/// the greedy seeder): of all candidates that could cover an otherwise open slot, only those breaking the least severe
/// wish stay (<see cref="KeywordWishSeverity"/>). The hour-driven pick of the caller runs on that subset only, so a wish is
/// never broken - nor a heavier one chosen - to give an employee hours. In Hard mode and without schedule commands the
/// candidates pass unchanged.
/// </summary>
public static class KeywordWishCandidates
{
    /// <summary>Severity of placing a shift of the given kind on the agent's day.</summary>
    /// <param name="context">The run's wizard context.</param>
    /// <param name="agentId">The employee.</param>
    /// <param name="date">Start day of the shift.</param>
    /// <param name="shiftTypeIndex">Kind of the shift (ShiftTypeInference).</param>
    public static int SeverityOf(CoreWizardContext context, string agentId, DateOnly date, int shiftTypeIndex)
        => IsActive(context)
            ? KeywordWishSeverity.Of(WizardKeywordWishes.For(context).BrokenRestriction(agentId, date, shiftTypeIndex))
            : KeywordWishSeverity.NoneBroken;

    /// <summary>The candidates whose placement breaks the least severe wish, in their original order.</summary>
    /// <param name="candidates">Candidates for one slot.</param>
    /// <param name="agentIdOf">Employee of a candidate.</param>
    /// <param name="context">The run's wizard context.</param>
    /// <param name="date">Start day of the slot.</param>
    /// <param name="shiftTypeIndex">Kind of the slot (ShiftTypeInference).</param>
    public static List<T> LeastBreaking<T>(
        IReadOnlyList<T> candidates, Func<T, string> agentIdOf, CoreWizardContext context, DateOnly date, int shiftTypeIndex)
    {
        if (!IsActive(context) || candidates.Count == 0)
        {
            return candidates.ToList();
        }

        var ranked = candidates.Select(c => (Candidate: c, Severity: SeverityOf(context, agentIdOf(c), date, shiftTypeIndex))).ToList();
        var least = ranked.Min(r => r.Severity);
        return ranked.Where(r => r.Severity == least).Select(r => r.Candidate).ToList();
    }

    private static bool IsActive(CoreWizardContext context)
        => context.KeywordMode == KeywordMode.Soft && context.ScheduleCommands.Count > 0;
}
