// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// Constant-time view on the shift preferences of a context, built once per operator invocation so a
/// per-candidate gate does not rescan the preference list.
/// </summary>
internal sealed class PreferenceIndex
{
    private readonly HashSet<(string AgentId, Guid ShiftRefId)> _preferred;
    private readonly HashSet<(string AgentId, Guid ShiftRefId)> _blacklisted;

    private PreferenceIndex(
        HashSet<(string, Guid)> preferred,
        HashSet<(string, Guid)> blacklisted)
    {
        _preferred = preferred;
        _blacklisted = blacklisted;
    }

    /// <param name="context">Wizard context carrying the master-data preferences</param>
    public static PreferenceIndex Build(CoreWizardContext context)
    {
        var preferred = new HashSet<(string, Guid)>();
        var blacklisted = new HashSet<(string, Guid)>();

        foreach (var preference in context.ShiftPreferences)
        {
            var key = (preference.AgentId, preference.ShiftRefId);
            if (preference.Kind == ShiftPreferenceKind.Blacklist)
            {
                blacklisted.Add(key);
            }
            else
            {
                preferred.Add(key);
            }
        }

        return new PreferenceIndex(preferred, blacklisted);
    }

    /// <param name="agentId">Employee</param>
    /// <param name="shiftRefId">Shift definition</param>
    public bool IsPreferred(string agentId, Guid shiftRefId)
        => _preferred.Count > 0 && _preferred.Contains((agentId, shiftRefId));

    /// <param name="agentId">Employee</param>
    /// <param name="shiftRefId">Shift definition</param>
    public bool IsBlacklisted(string agentId, Guid shiftRefId)
        => _blacklisted.Count > 0 && _blacklisted.Contains((agentId, shiftRefId));
}
