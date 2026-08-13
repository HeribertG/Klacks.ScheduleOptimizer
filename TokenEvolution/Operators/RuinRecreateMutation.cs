// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// M8 ruin and recreate: removes every non-locked token inside one contiguous calendar window and
/// lets the deterministic package-aware coverage sweep rebuild the window from scratch — the
/// literature's macro move (Schrimpf's ruin-and-recreate, Ceschia's multi-day swaps, the
/// Pisinger/Ropke 10 to 30 percent rule) whose ruined intermediate state is never compared; only
/// the rebuilt end state enters the lexicographic comparison. The window spans SEVERAL days on
/// purpose: destroying too little cannot escape a basin in tightly coupled rosters (Legrain,
/// INRC-II — a one-week destroy made no improvement possible during repair).
/// </summary>
/// <param name="repair">Repair whose coverage sweep rebuilds the ruined window.</param>
public sealed class RuinRecreateMutation
{
    private readonly TokenRepair _repair;

    public RuinRecreateMutation(TokenRepair repair)
    {
        _repair = repair;
    }

    /// <summary>
    /// Ruins one random window of <paramref name="windowMinDays"/> to
    /// <paramref name="windowMaxDays"/> days (clamped to the period) and rebuilds it.
    /// </summary>
    public CoreScenario Apply(TokenOperatorContext context, int windowMinDays, int windowMaxDays)
    {
        var wizard = context.Wizard;
        var periodDays = wizard.PeriodUntil.DayNumber - wizard.PeriodFrom.DayNumber + 1;
        if (periodDays < 1)
        {
            return TokenSwapMutation.CloneScenario(context.Primary, context.Primary.Tokens.ToList());
        }

        var upper = Math.Min(Math.Max(windowMaxDays, 1), periodDays);
        var lower = Math.Min(Math.Max(windowMinDays, 1), upper);
        var length = lower + (upper > lower ? context.Rng.Next(upper - lower + 1) : 0);
        var windowStart = wizard.PeriodFrom.AddDays(context.Rng.Next(periodDays - length + 1));
        var windowEnd = windowStart.AddDays(length - 1);

        var kept = context.Primary.Tokens
            .Where(t => t.IsLocked || t.Date < windowStart || t.Date > windowEnd)
            .ToList();
        if (kept.Count == context.Primary.Tokens.Count)
        {
            return TokenSwapMutation.CloneScenario(context.Primary, kept);
        }

        var ruined = TokenSwapMutation.CloneScenario(context.Primary, kept);
        return _repair.FillAllUnderSupply(ruined, wizard, context.Rng);
    }
}
