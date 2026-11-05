// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Constraints.Keywords;

/// <summary>
/// Combines several schedule commands of one (agent, day) on the set of allowed shift kinds {Early, Late, Night}:
/// OnlyX keeps only X, NoX removes X, FREE removes all, -FREE (NotFree) removes none. Three kinds left = NotFree,
/// two = No&lt;missing kind&gt;, one = Only&lt;kind&gt;, none = Free. Single source of the combination for the wizards,
/// the harmonizer and the Api checks.
/// </summary>
public static class KeywordDayRule
{
    /// <summary>Allowed kinds (<see cref="KeywordShiftKinds"/>) after applying every keyword; all classified kinds when empty.</summary>
    /// <param name="keywords">The recognized keywords of one (agent, day).</param>
    public static int AllowedKinds(IEnumerable<ScheduleCommandKeyword> keywords)
    {
        var allowed = KeywordShiftKinds.AllClassified;
        foreach (var keyword in keywords)
        {
            allowed = Restrict(allowed, keyword);
        }

        return allowed;
    }

    /// <summary>Combined keyword of the given commands, or null when there are none.</summary>
    /// <param name="keywords">The recognized keywords of one (agent, day), in any order.</param>
    public static ScheduleCommandKeyword? Combine(IEnumerable<ScheduleCommandKeyword> keywords)
    {
        var any = false;
        var allowed = KeywordShiftKinds.AllClassified;
        foreach (var keyword in keywords)
        {
            any = true;
            allowed = Restrict(allowed, keyword);
        }

        return any ? ToKeyword(allowed) : null;
    }

    /// <summary>True when a day governed by the keyword may carry a shift of the given kind index.</summary>
    /// <param name="keyword">Combined keyword of the day.</param>
    /// <param name="shiftTypeIndex">Shift kind index (ShiftTypeInference).</param>
    public static bool Allows(ScheduleCommandKeyword keyword, int shiftTypeIndex)
        => (Restrict(KeywordShiftKinds.AllClassified, keyword) & KeywordShiftKinds.FromShiftTypeIndex(shiftTypeIndex))
            != KeywordShiftKinds.None;

    /// <summary>Keyword that expresses the allowed classified kinds.</summary>
    /// <param name="allowed">Allowed kinds (<see cref="KeywordShiftKinds"/>).</param>
    public static ScheduleCommandKeyword ToKeyword(int allowed) => (allowed & KeywordShiftKinds.AllClassified) switch
    {
        KeywordShiftKinds.None => ScheduleCommandKeyword.Free,
        KeywordShiftKinds.Early => ScheduleCommandKeyword.OnlyEarly,
        KeywordShiftKinds.Late => ScheduleCommandKeyword.OnlyLate,
        KeywordShiftKinds.Night => ScheduleCommandKeyword.OnlyNight,
        KeywordShiftKinds.Late | KeywordShiftKinds.Night => ScheduleCommandKeyword.NoEarly,
        KeywordShiftKinds.Early | KeywordShiftKinds.Night => ScheduleCommandKeyword.NoLate,
        KeywordShiftKinds.Early | KeywordShiftKinds.Late => ScheduleCommandKeyword.NoNight,
        _ => ScheduleCommandKeyword.NotFree,
    };

    /// <summary>True for the keywords that only keep one kind (EARLY, LATE, NIGHT).</summary>
    /// <param name="keyword">The keyword.</param>
    public static bool IsOnly(ScheduleCommandKeyword keyword)
        => keyword is ScheduleCommandKeyword.OnlyEarly or ScheduleCommandKeyword.OnlyLate or ScheduleCommandKeyword.OnlyNight;

    private static int Restrict(int allowed, ScheduleCommandKeyword keyword) => keyword switch
    {
        ScheduleCommandKeyword.Free => KeywordShiftKinds.None,
        ScheduleCommandKeyword.OnlyEarly => allowed & KeywordShiftKinds.Early,
        ScheduleCommandKeyword.OnlyLate => allowed & KeywordShiftKinds.Late,
        ScheduleCommandKeyword.OnlyNight => allowed & KeywordShiftKinds.Night,
        ScheduleCommandKeyword.NoEarly => allowed & ~KeywordShiftKinds.Early,
        ScheduleCommandKeyword.NoLate => allowed & ~KeywordShiftKinds.Late,
        ScheduleCommandKeyword.NoNight => allowed & ~KeywordShiftKinds.Night,
        _ => allowed,
    };
}
