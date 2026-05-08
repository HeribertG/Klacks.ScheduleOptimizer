// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Committee.Agents;

/// <summary>
/// Compares the post-swap symbols against each row's <c>PreferredShiftSymbols</c>. Vetoes when
/// the swap moves both rows from a preferred onto a non-preferred shift. Approves when both
/// rows transition onto a preferred shift. Abstains for Free transitions, when a row has no
/// preference data, or when the swap mixes preferred/non-preferred outcomes.
/// </summary>
public sealed class PreferenceConstraintAgent : IConstraintAgent
{
    public string Name => "Preference";

    public ConstraintAgentVerdict Evaluate(HarmonyBitmap before, PlanCellSwap swap)
    {
        var cellA = before.GetCell(swap.RowA, swap.DayA);
        var cellB = before.GetCell(swap.RowB, swap.DayB);
        var rowAPrefs = before.Rows[swap.RowA].PreferredShiftSymbols;
        var rowBPrefs = before.Rows[swap.RowB].PreferredShiftSymbols;

        if (rowAPrefs.Count == 0 && rowBPrefs.Count == 0)
        {
            return new ConstraintAgentVerdict(Name, ConstraintAgentVote.Abstain, "no preference data on either row");
        }

        var rowAResult = ClassifyTransition(cellA.Symbol, cellB.Symbol, rowAPrefs);
        var rowBResult = ClassifyTransition(cellB.Symbol, cellA.Symbol, rowBPrefs);

        if (rowAResult == TransitionResult.Worsens && rowBResult == TransitionResult.Worsens)
        {
            return new ConstraintAgentVerdict(Name, ConstraintAgentVote.Veto, "both rows lose a preferred shift");
        }

        if (rowAResult == TransitionResult.Improves && rowBResult == TransitionResult.Improves)
        {
            return new ConstraintAgentVerdict(Name, ConstraintAgentVote.Approve, "both rows gain a preferred shift");
        }

        return new ConstraintAgentVerdict(Name, ConstraintAgentVote.Abstain, "mixed or neutral preference impact");
    }

    private static TransitionResult ClassifyTransition(CellSymbol oldSymbol, CellSymbol newSymbol, IReadOnlySet<CellSymbol> preferences)
    {
        if (oldSymbol == newSymbol) return TransitionResult.Neutral;
        if (preferences.Count == 0) return TransitionResult.Neutral;
        if (oldSymbol == CellSymbol.Free || newSymbol == CellSymbol.Free) return TransitionResult.Neutral;
        if (oldSymbol == CellSymbol.Break || newSymbol == CellSymbol.Break) return TransitionResult.Neutral;

        var oldPreferred = preferences.Contains(oldSymbol);
        var newPreferred = preferences.Contains(newSymbol);
        if (oldPreferred && !newPreferred) return TransitionResult.Worsens;
        if (!oldPreferred && newPreferred) return TransitionResult.Improves;
        return TransitionResult.Neutral;
    }

    private enum TransitionResult
    {
        Improves,
        Worsens,
        Neutral,
    }
}
