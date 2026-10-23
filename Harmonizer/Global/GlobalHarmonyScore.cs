// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Harmonizer.Global;

/// <summary>
/// Harmony of the whole plan measured BETWEEN the rows (the second instance next to the rules). The per-row harmony
/// score judges each agent's own sequence; this score judges how the agents fit together, like voices in an ensemble.
/// Every value is in [0..1], higher is better.
/// </summary>
/// <param name="Total">Weighted sum of <paramref name="Rhythm"/> and <paramref name="Fairness"/>.</param>
/// <param name="Rhythm">How well the working rows are shifted copies of one common pattern (a canon).</param>
/// <param name="Fairness">How evenly early, late, night and weekend work is shared, as a share of each row's own
/// working days, so part-time rows are not counted as unfair.</param>
public sealed record GlobalHarmonyScore(double Total, double Rhythm, double Fairness)
{
    public static GlobalHarmonyScore Neutral { get; } = new(1d, 1d, 1d);
}
