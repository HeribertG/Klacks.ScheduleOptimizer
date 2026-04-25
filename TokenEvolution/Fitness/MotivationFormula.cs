// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

/// <summary>
/// Computes the motivation of an agent for a specific shift in the range [0, 1].
/// Formula: (0.6·hunger + 0.3·greed + 0.1·(1-satiety)) · (1-disgust).
/// - hunger  = how far from FullTime the agent still is (wants hours)
/// - greed   = 1 if the shift is in Preferred preferences, else 0
/// - satiety = inverse of hunger (hours already covered)
/// - disgust = 1 if the shift is in Blacklist preferences, else 0
/// </summary>
public static class MotivationFormula
{
    public static double Compute(
        CoreAgent agent,
        Guid shiftRefId,
        decimal plannedAdditionalHours,
        IReadOnlyList<CoreShiftPreference> preferences)
    {
        var hunger = ComputeHunger(agent, plannedAdditionalHours);
        var greed = HasPreference(agent.Id, shiftRefId, preferences, ShiftPreferenceKind.Preferred) ? 1.0 : 0.0;
        var satiety = ComputeSatiety(agent);
        var disgust = HasPreference(agent.Id, shiftRefId, preferences, ShiftPreferenceKind.Blacklist) ? 1.0 : 0.0;

        var motivation = ((0.6 * hunger) + (0.3 * greed) + (0.1 * (1 - Math.Min(1, satiety)))) * (1 - Math.Min(1, disgust));
        return Math.Clamp(motivation, 0, 1);
    }

    private static double ComputeHunger(CoreAgent agent, decimal plannedAdditionalHours)
    {
        if (agent.FullTime <= 0)
        {
            return 0;
        }

        var planned = agent.CurrentHours + (double)plannedAdditionalHours;
        var deficit = agent.FullTime - planned;
        return Math.Max(0, Math.Min(1, deficit / agent.FullTime));
    }

    private static double ComputeSatiety(CoreAgent agent)
    {
        if (agent.FullTime <= 0)
        {
            return 1;
        }

        return agent.CurrentHours / agent.FullTime;
    }

    private static bool HasPreference(
        string agentId, Guid shiftRefId, IReadOnlyList<CoreShiftPreference> preferences, ShiftPreferenceKind kind)
    {
        foreach (var pref in preferences)
        {
            if (pref.AgentId == agentId && pref.ShiftRefId == shiftRefId && pref.Kind == kind)
            {
                return true;
            }
        }

        return false;
    }
}
