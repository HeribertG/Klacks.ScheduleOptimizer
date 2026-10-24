// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Conductor;

namespace Klacks.ScheduleOptimizer.Harmonizer.Global;

/// <summary>
/// Second instance for Wizard 2: wraps the rule validator and additionally rejects every swap that would take the
/// global harmony of the plan (rhythm and fairness between the rows, <see cref="GlobalHarmonyEvaluator"/>) below a
/// fixed floor - the harmony of the plan the job started from. Every move the conductor, the block swap and the
/// stochastic mutation make passes through the validator, so no individual of the run, and therefore not the
/// result, ever ends below the start harmony ("nothing gets worse"). The rules keep their veto: the harmony is only
/// asked when the inner validator already accepts the move.
/// <para>
/// A block swap is checked day by day on the partly swapped plan, so the guard is strict there: a block whose
/// intermediate state dips below the floor is rejected even when its end state would not.
/// </para>
/// </summary>
/// <param name="inner">Rule validator (locks, hard rules, planning rules)</param>
/// <param name="harmony">Global harmony measure</param>
/// <param name="floor">Harmony total the plan must not fall below</param>
public sealed class HarmonyGuardReplaceValidator(IReplaceValidator inner, GlobalHarmonyEvaluator harmony, double floor) : IReplaceValidator
{
    private const double Epsilon = 1e-9;

    private readonly IReplaceValidator _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly GlobalHarmonyEvaluator _harmony = harmony ?? throw new ArgumentNullException(nameof(harmony));

    /// <summary>Harmony total the plan must not fall below.</summary>
    public double Floor { get; } = floor;

    /// <summary>Builds the guard with the harmony of <paramref name="start"/> as floor.</summary>
    public static HarmonyGuardReplaceValidator ForStart(IReplaceValidator inner, HarmonyBitmap start)
    {
        ArgumentNullException.ThrowIfNull(start);
        var harmony = new GlobalHarmonyEvaluator();
        return new HarmonyGuardReplaceValidator(inner, harmony, harmony.Evaluate(start).Total);
    }

    public bool IsValid(HarmonyBitmap bitmap, ReplaceMove move)
    {
        if (!_inner.IsValid(bitmap, move))
        {
            return false;
        }

        var cellA = bitmap.GetCell(move.RowA, move.Day);
        var cellB = bitmap.GetCell(move.RowB, move.Day);
        if (cellA.Symbol == cellB.Symbol)
        {
            return true;
        }

        bitmap.SetCell(move.RowA, move.Day, cellB);
        bitmap.SetCell(move.RowB, move.Day, cellA);
        try
        {
            return _harmony.Evaluate(bitmap).Total >= Floor - Epsilon;
        }
        finally
        {
            bitmap.SetCell(move.RowA, move.Day, cellA);
            bitmap.SetCell(move.RowB, move.Day, cellB);
        }
    }
}
