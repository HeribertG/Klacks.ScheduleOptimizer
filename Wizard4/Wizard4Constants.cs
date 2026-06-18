// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Wizard4;

/// <summary>
/// Tunable constants for the Wizard-4 runner. Kept out of the engine core (which is pure) so the
/// hosting decisions carry no magic numbers.
/// </summary>
public static class Wizard4Constants
{
    /// <summary>
    /// Minimum composite-scalar gain over the snapshot before a candidate is worth proposing. Below
    /// this the run found no meaningful improvement and no candidate scenario is created (avoids
    /// cosmetic-churn proposals).
    /// </summary>
    public const double MinImprovement = 0.005;
}
