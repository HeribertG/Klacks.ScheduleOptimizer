// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

/// <summary>
/// Order loyalty (rule 10) as a number: how often an employee changes the order between two
/// consecutive assignments. The measure separates the two questions the rule really asks — staying on
/// one order INSIDE a package, and not hopping between orders from package to package — because the
/// first is the rule itself and the second only its monthly shadow. Both are folded back into a single
/// score with <see cref="WithinBlockWeight"/> and <see cref="AcrossBlockWeight"/>, so a plan can never
/// buy an in-package improvement with an equally large cross-package regression.
/// <para>
/// A uniform location context — every token null, or every token the same order — yields no switches
/// on either side and therefore the score 1, exactly the value the flat predecessor returned. Plans
/// without an order dimension are unaffected.
/// </para>
/// </summary>
public static class LocationContinuity
{
    /// <summary>Share of the score carried by changes inside one package.</summary>
    public const double WithinBlockWeight = 0.75;

    /// <summary>Share of the score carried by changes from one package to the next.</summary>
    public const double AcrossBlockWeight = 0.25;

    /// <summary>
    /// True when two assignments of the same employee belong to the same package: packages are maximal
    /// runs of consecutive working dates, the same block definition the rotation term uses.
    /// </summary>
    /// <param name="previous">Earlier assignment in the employee's date order</param>
    /// <param name="current">Assignment that follows it</param>
    public static bool IsSamePackage(CoreToken previous, CoreToken current)
        => current.Date.DayNumber - previous.Date.DayNumber <= 1;

    /// <summary>Weighted order-loyalty score of a plan, 1 when no employee ever changes the order.</summary>
    /// <param name="scenario">Plan to measure</param>
    public static double Score(CoreScenario scenario)
    {
        var withinPairs = 0;
        var withinSwitches = 0;
        var acrossPairs = 0;
        var acrossSwitches = 0;

        foreach (var perAgent in scenario.Tokens.GroupBy(t => t.AgentId, StringComparer.Ordinal))
        {
            var ordered = perAgent.OrderBy(t => t.Date).ThenBy(t => t.StartAt).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                var changed = !string.Equals(
                    ordered[i].LocationContext, ordered[i - 1].LocationContext, StringComparison.Ordinal);

                if (IsSamePackage(ordered[i - 1], ordered[i]))
                {
                    withinPairs++;
                    withinSwitches += changed ? 1 : 0;
                }
                else
                {
                    acrossPairs++;
                    acrossSwitches += changed ? 1 : 0;
                }
            }
        }

        return Combine(withinPairs, withinSwitches, acrossPairs, acrossSwitches);
    }

    /// <summary>
    /// Folds the two counted dimensions into the score. Kept public so an operator can score a planned
    /// swap from its own incremental counters instead of rebuilding the whole measurement.
    /// </summary>
    /// <param name="withinPairs">Consecutive pairs inside a package</param>
    /// <param name="withinSwitches">Order changes among them</param>
    /// <param name="acrossPairs">Pairs spanning a package boundary</param>
    /// <param name="acrossSwitches">Order changes among them</param>
    public static double Combine(int withinPairs, int withinSwitches, int acrossPairs, int acrossSwitches)
    {
        var within = withinPairs == 0 ? 1 : 1.0 - (withinSwitches / (double)withinPairs);
        var across = acrossPairs == 0 ? 1 : 1.0 - (acrossSwitches / (double)acrossPairs);
        return (within * WithinBlockWeight) + (across * AcrossBlockWeight);
    }
}
