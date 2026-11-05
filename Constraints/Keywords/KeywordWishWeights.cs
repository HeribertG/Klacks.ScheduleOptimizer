// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.TokenEvolution;

namespace Klacks.ScheduleOptimizer.Constraints.Keywords;

/// <summary>
/// Weights of the keyword-wish tier: one broken FREE weighs more than one broken OnlyX, which weighs more than one
/// broken NoX; an unfulfilled positive wish weighs least. Defaults live in <see cref="TokenEvolutionConfig"/>.
/// </summary>
/// <param name="Free">Weight of a broken FREE.</param>
/// <param name="Only">Weight of a broken OnlyX.</param>
/// <param name="No">Weight of a broken NoX.</param>
/// <param name="Unfulfilled">Weight of an unfulfilled positive wish.</param>
public sealed record KeywordWishWeights(double Free, double Only, double No, double Unfulfilled)
{
    public static KeywordWishWeights Default { get; } = new(
        TokenEvolutionConfig.DefaultKeywordWishFreeWeight,
        TokenEvolutionConfig.DefaultKeywordWishOnlyWeight,
        TokenEvolutionConfig.DefaultKeywordWishNoWeight,
        TokenEvolutionConfig.DefaultKeywordWishUnfulfilledWeight);

    /// <summary>Weighted penalty of one day's verdict.</summary>
    /// <param name="verdict">Verdict of the day.</param>
    public double Of(KeywordDayVerdict verdict)
        => WeightOf(verdict.Broken) + (verdict.Unfulfilled ? Unfulfilled : 0);

    /// <summary>Weight of one outcome.</summary>
    /// <param name="outcome">The outcome; null weighs nothing.</param>
    public double WeightOf(KeywordWishOutcome? outcome) => outcome switch
    {
        KeywordWishOutcome.BrokenFree => Free,
        KeywordWishOutcome.BrokenOnly => Only,
        KeywordWishOutcome.BrokenNo => No,
        KeywordWishOutcome.Unfulfilled => Unfulfilled,
        _ => 0,
    };
}
