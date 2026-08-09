// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Turns the head of an existing plan into locked works so a replanning run can only touch the tail:
/// every assignment before the replan date becomes a <see cref="CoreLockedWork"/>, which the seeding
/// strategies convert into immutable genome tokens (<see cref="LockedTokenFactory"/>) that no
/// operator, repair or handover pass may move. A master-data change effective from day X therefore
/// replans day X onward and provably leaves everything before X untouched — the frozen head still
/// takes part in rest-hour, consecutive-day and collision checks at the seam.
/// </summary>
public static class FrozenPrefix
{
    /// <summary>
    /// The locked works representing every assignment of the plan strictly before the replan date,
    /// in a deterministic order. Pass the result to <c>CoreWizardContext.LockedWorks</c> (merged
    /// with any locked works the context already carries) when building the replanning context.
    /// </summary>
    /// <param name="existingPlan">Plan whose head is to be frozen; never modified</param>
    /// <param name="replanFrom">First day the replanning run may change</param>
    public static IReadOnlyList<CoreLockedWork> BuildLockedWorks(
        CoreScenario existingPlan, DateOnly replanFrom)
    {
        var frozen = new List<CoreLockedWork>();
        foreach (var token in existingPlan.Tokens
                     .Where(t => t.Date < replanFrom)
                     .OrderBy(t => t.Date)
                     .ThenBy(t => t.StartAt)
                     .ThenBy(t => t.AgentId, StringComparer.Ordinal))
        {
            frozen.Add(new CoreLockedWork(
                WorkId: token.WorkIds.Count > 0 ? token.WorkIds[0] : SyntheticWorkId(token),
                AgentId: token.AgentId,
                Date: token.Date,
                ShiftTypeIndex: token.ShiftTypeIndex,
                TotalHours: token.TotalHours,
                StartAt: token.StartAt,
                EndAt: token.EndAt,
                ShiftRefId: token.ShiftRefId,
                LocationContext: token.LocationContext)
            {
                Surcharges = token.Surcharges,
            });
        }

        return frozen;
    }

    private static string SyntheticWorkId(CoreToken token)
        => $"frozen-{token.Date:yyyyMMdd}-{token.ShiftRefId:N}-{token.AgentId}";
}
