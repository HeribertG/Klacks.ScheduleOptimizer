// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Verdict;

/// <summary>
/// One explained soft ingredient of the verdict score. The weights ARE the judgement, so every
/// term carries its weight, its raw measurement and its explanation instead of hiding them.
/// </summary>
/// <param name="Name">Stable identifier of the term (e.g. "compactness")</param>
/// <param name="Weight">Normalized weight the term enters the soft score with</param>
/// <param name="RawScore">Raw measurement of the term in [0, 1] before weighting</param>
/// <param name="Contribution">Weight times raw score — the term's share of the soft score</param>
/// <param name="Explanation">Human-readable English sentence stating what was measured</param>
public sealed record VerdictTerm(
    string Name,
    double Weight,
    double RawScore,
    double Contribution,
    string Explanation);
