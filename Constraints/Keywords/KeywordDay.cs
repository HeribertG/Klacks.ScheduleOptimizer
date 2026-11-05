// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Constraints.Keywords;

/// <summary>
/// The schedule commands of one (agent, day) reduced to what the wish rules need. The restriction comes from the
/// combined keyword: Free (also a combination that leaves no kind) is the FREE tier, a combination containing an entered
/// OnlyX is the OnlyX tier, every other remaining restriction (only NoX entered) is the NoX tier. The positive wish comes
/// only from what the employee entered: an OnlyX that survives the combination wants a shift of that kind, otherwise an
/// entered -FREE wants any shift. A combined Free never attracts.
/// </summary>
/// <param name="Combined">Combined keyword of the day (<see cref="KeywordDayRule.Combine"/>).</param>
/// <param name="AllowedKinds">Kinds the day allows (<see cref="KeywordShiftKinds"/>); an unclassified shift counts as
/// allowed while at least two classified kinds are.</param>
/// <param name="RestrictionOutcome">Outcome when the day's restriction is broken; null when the day restricts nothing.</param>
/// <param name="PositiveWish">The entered positive wish (NotFree or the surviving OnlyX); null when none attracts.</param>
public sealed record KeywordDay(
    ScheduleCommandKeyword Combined,
    int AllowedKinds,
    KeywordWishOutcome? RestrictionOutcome,
    ScheduleCommandKeyword? PositiveWish)
{
    private const int MinClassifiedKindsForUnclassified = 2;

    /// <summary>Kinds that fulfil the positive wish; <see cref="KeywordShiftKinds.None"/> when there is none.</summary>
    public int WantedKinds => PositiveWish switch
    {
        null => KeywordShiftKinds.None,
        ScheduleCommandKeyword.NotFree => KeywordShiftKinds.AnyWork,
        _ => AllowedKinds & KeywordShiftKinds.AllClassified,
    };

    /// <summary>True when the day allows every kind in the mask.</summary>
    /// <param name="workedKinds">Kinds worked that day (<see cref="KeywordShiftKinds"/>).</param>
    public bool Allows(int workedKinds) => (workedKinds & ~AllowedKinds) == KeywordShiftKinds.None;

    /// <summary>The same day without its positive wish (the employee cannot work it anyway, e.g. an absence).</summary>
    public KeywordDay WithoutPositiveWish() => PositiveWish is null ? this : this with { PositiveWish = null };

    /// <summary>Reduces the entered keywords of one (agent, day); null when there are none.</summary>
    /// <param name="keywords">The recognized keywords, in any order.</param>
    public static KeywordDay? From(IEnumerable<ScheduleCommandKeyword> keywords)
    {
        var entered = keywords.ToList();
        if (entered.Count == 0)
        {
            return null;
        }

        var allowed = KeywordDayRule.AllowedKinds(entered);
        var combined = KeywordDayRule.ToKeyword(allowed);
        if (CountKinds(allowed) >= MinClassifiedKindsForUnclassified)
        {
            allowed |= KeywordShiftKinds.Unclassified;
        }

        return new KeywordDay(combined, allowed, RestrictionOf(combined, entered), PositiveWishOf(combined, entered));
    }

    private static KeywordWishOutcome? RestrictionOf(ScheduleCommandKeyword combined, List<ScheduleCommandKeyword> entered)
    {
        if (combined == ScheduleCommandKeyword.NotFree)
        {
            return null;
        }

        if (combined == ScheduleCommandKeyword.Free)
        {
            return KeywordWishOutcome.BrokenFree;
        }

        return entered.Any(KeywordDayRule.IsOnly) ? KeywordWishOutcome.BrokenOnly : KeywordWishOutcome.BrokenNo;
    }

    private static ScheduleCommandKeyword? PositiveWishOf(ScheduleCommandKeyword combined, List<ScheduleCommandKeyword> entered)
    {
        if (combined == ScheduleCommandKeyword.Free)
        {
            return null;
        }

        if (KeywordDayRule.IsOnly(combined) && entered.Contains(combined))
        {
            return combined;
        }

        return entered.Contains(ScheduleCommandKeyword.NotFree) ? ScheduleCommandKeyword.NotFree : null;
    }

    private static int CountKinds(int allowed)
    {
        var count = 0;
        foreach (var kind in new[] { KeywordShiftKinds.Early, KeywordShiftKinds.Late, KeywordShiftKinds.Night })
        {
            if ((allowed & kind) != KeywordShiftKinds.None)
            {
                count++;
            }
        }

        return count;
    }
}
