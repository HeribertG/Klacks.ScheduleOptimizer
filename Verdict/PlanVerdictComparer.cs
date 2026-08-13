// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Verdict;

/// <summary>
/// Decides whether a candidate verdict beats the incumbent one. The incumbent (the state before
/// the optimization) wins every tie: a plan is only replaced by something strictly better, and at
/// equal score the plan with fewer rest findings wins — a forgiven scratch must never become the
/// cheap way to the top (Goodhart guard of the stage design).
/// </summary>
public static class PlanVerdictComparer
{
    /// <summary>True when the candidate strictly beats the incumbent.</summary>
    /// <param name="candidate">Verdict of the newly produced plan</param>
    /// <param name="incumbent">Verdict of the state to beat (e.g. the plan before optimizing)</param>
    public static bool IsImprovement(PlanVerdict candidate, PlanVerdict incumbent)
    {
        if (candidate.Score != incumbent.Score)
        {
            return candidate.Score > incumbent.Score;
        }

        return candidate.Findings.Count < incumbent.Findings.Count;
    }
}
