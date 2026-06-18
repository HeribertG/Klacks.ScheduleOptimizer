// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Objective;

/// <summary>
/// Hand-set v1 weights and normalisation constants for the composite "Gesamtzustand" objective.
/// All values live here so the scoring code carries no magic numbers; they are the knobs a future
/// preference-learner (deferred, not v1) would fit. The hard protection (gate floors, per-agent
/// blacklist cap, worst-agent floor, churn cap) lives in the gate/guards, never in these weights —
/// a weight can never "buy" a hard violation.
/// </summary>
public static class ObjectiveConstants
{
    /// <summary>Top-level weight of the error/warning term (cleanliness of the plan). Dominant.</summary>
    public const double WeightFehler = 0.45;

    /// <summary>Top-level weight of the hours-alignment term (Owner priority #1).</summary>
    public const double WeightStundenabgleich = 0.33;

    /// <summary>Top-level weight of the satisfaction term (v1 = preferences only; continuity/load-fairness deferred).</summary>
    public const double WeightZufriedenheit = 0.22;

    /// <summary>Relative deviation that maps the hours sub-score to 0. 1.0 = a full-target miss scores 0 (no saturation zone, unlike the W2 scale 3.0 that saturates at 33%).</summary>
    public const double HoursDeviationFull = 1.0;

    /// <summary>Effective target at or below this (hours) excludes an agent from the hours reward (no free-parking on zero-target agents). Overload is still caught by the gate's MaximumHoursExceeded floor.</summary>
    public const double ZeroTargetEpsilon = 1e-6;

    /// <summary>Weight of the preferred-shift axis inside a per-agent preference score.</summary>
    public const double PreferenceWeightPreferred = 0.6;

    /// <summary>Weight of the blacklist-avoidance axis inside a per-agent preference score (avoiding hated shifts matters more than getting loved ones).</summary>
    public const double PreferenceWeightBlacklist = 1.0;

    /// <summary>Blend between the egalitarian mean (alpha) and the worst-off agent (1 - alpha) when aggregating per-agent scores.</summary>
    public const double WorstOffBlendAlpha = 0.7;

    /// <summary>Hard churn-ratio cap (gate-cap, applied at the Api/scenario layer): a candidate above this is only admissible if it closes a hard violation.</summary>
    public const double ChurnRatioCap = 0.25;

    /// <summary>Tolerance for worst-off no-regression guards (runner-side, vs baseline).</summary>
    public const double GuardEpsilon = 0.02;
}
