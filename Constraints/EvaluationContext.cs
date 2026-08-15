// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using System.Runtime.CompilerServices;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Constraints;

/// <summary>
/// Frozen lookups derived once per <see cref="CoreWizardContext"/>. The constraint and fitness layers
/// rebuilt the same dictionaries for every genome they scored - with a population of hundreds over
/// hundreds of generations that is the single largest allocation source of a wizard run, even though
/// none of the inputs change during the run. Instances are cached per context in a
/// <see cref="ConditionalWeakTable{TKey,TValue}"/>: the lookup is O(1), the memory is released with the
/// context, and a race during creation is harmless because the factory has no side effects and exactly
/// one instance wins. Everything here is read-only after construction, so it is safe to read from
/// several evaluation threads at once.
/// </summary>
public sealed class EvaluationContext
{
    private static readonly ConditionalWeakTable<CoreWizardContext, EvaluationContext> Cache = new();

    private EvaluationContext(CoreWizardContext context)
    {
        AgentsById = BuildAgentsById(context);
        ContractDayByAgentDate = BuildContractDays(context);
        CommandsByAgentDate = BuildCommands(context);
        BreakBlockersByAgent = BuildBreakBlockers(context);
        BlacklistedShiftsByAgent = BuildBlacklists(context);
        AgentsWithMaximumHours = context.Agents.Where(a => a.MaximumHours > 0).ToList();
        (SlotCapacities, SlotsByKey, LocationContextByShift) = BuildSlots(context);
    }

    /// <summary>Agents by id; first entry wins on duplicates.</summary>
    public IReadOnlyDictionary<string, CoreAgent> AgentsById { get; }

    /// <summary>
    /// Contract day per (agent, date). The former lookup took the FIRST match of a duplicate group, so
    /// this build uses TryAdd: same result on the current context builders, which produce exactly one
    /// contract day per pair, and no exception should a duplicate ever appear.
    /// </summary>
    public IReadOnlyDictionary<(string AgentId, DateOnly Date), CoreContractDay> ContractDayByAgentDate { get; }

    /// <summary>Schedule commands per (agent, date), in input order.</summary>
    public IReadOnlyDictionary<(string AgentId, DateOnly Date), List<CoreScheduleCommand>> CommandsByAgentDate { get; }

    /// <summary>Break blockers per agent, so a check no longer scans every blocker for every assignment.</summary>
    public IReadOnlyDictionary<string, List<CoreBreakBlocker>> BreakBlockersByAgent { get; }

    /// <summary>Blacklisted shift definitions per agent, for the hard shift-preference veto.</summary>
    public IReadOnlyDictionary<string, HashSet<Guid>> BlacklistedShiftsByAgent { get; }

    /// <summary>Agents carrying a maximum-hours cap, in the order of the context's agent list.</summary>
    public IReadOnlyList<CoreAgent> AgentsWithMaximumHours { get; }

    /// <summary>Required assignments per (shift, date), parsed once instead of per check.</summary>
    public IReadOnlyDictionary<(Guid ShiftRefId, DateOnly Date), int> SlotCapacities { get; }

    /// <summary>Shift definition per (shift, date); first match wins, mirroring the former linear search.</summary>
    public IReadOnlyDictionary<(Guid ShiftRefId, DateOnly Date), CoreShift> SlotsByKey { get; }

    /// <summary>
    /// Order identity per shift definition, for the producers that hold a shift reference but no slot
    /// object (repair, warm start). A shift definition belongs to the same order on every date, so the
    /// date is not part of the key; shifts without a location context are absent from the table.
    /// </summary>
    public IReadOnlyDictionary<Guid, string> LocationContextByShift { get; }

    /// <summary>Returns the frozen lookups of <paramref name="context"/>, building them on first use.</summary>
    public static EvaluationContext For(CoreWizardContext context)
        => Cache.GetValue(context, static ctx => new EvaluationContext(ctx));

    private static Dictionary<string, CoreAgent> BuildAgentsById(CoreWizardContext context)
    {
        var result = new Dictionary<string, CoreAgent>(context.Agents.Count, StringComparer.Ordinal);
        foreach (var agent in context.Agents)
        {
            result.TryAdd(agent.Id, agent);
        }

        return result;
    }

    private static Dictionary<(string, DateOnly), CoreContractDay> BuildContractDays(CoreWizardContext context)
    {
        var result = new Dictionary<(string, DateOnly), CoreContractDay>(context.ContractDays.Count);
        foreach (var day in context.ContractDays)
        {
            result.TryAdd((day.AgentId, day.Date), day);
        }

        return result;
    }

    private static Dictionary<(string, DateOnly), List<CoreScheduleCommand>> BuildCommands(CoreWizardContext context)
    {
        var result = new Dictionary<(string, DateOnly), List<CoreScheduleCommand>>();
        foreach (var command in context.ScheduleCommands)
        {
            var key = (command.AgentId, command.Date);
            if (!result.TryGetValue(key, out var list))
            {
                list = [];
                result[key] = list;
            }

            list.Add(command);
        }

        return result;
    }

    private static Dictionary<string, List<CoreBreakBlocker>> BuildBreakBlockers(CoreWizardContext context)
    {
        var result = new Dictionary<string, List<CoreBreakBlocker>>(StringComparer.Ordinal);
        foreach (var blocker in context.BreakBlockers)
        {
            if (!result.TryGetValue(blocker.AgentId, out var list))
            {
                list = [];
                result[blocker.AgentId] = list;
            }

            list.Add(blocker);
        }

        return result;
    }

    private static Dictionary<string, HashSet<Guid>> BuildBlacklists(CoreWizardContext context)
    {
        var result = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
        foreach (var preference in context.ShiftPreferences)
        {
            if (preference.Kind != ShiftPreferenceKind.Blacklist)
            {
                continue;
            }

            if (!result.TryGetValue(preference.AgentId, out var set))
            {
                set = [];
                result[preference.AgentId] = set;
            }

            set.Add(preference.ShiftRefId);
        }

        return result;
    }

    private static (Dictionary<(Guid, DateOnly), int> Capacities,
        Dictionary<(Guid, DateOnly), CoreShift> Slots,
        Dictionary<Guid, string> Locations)
        BuildSlots(CoreWizardContext context)
    {
        var capacities = new Dictionary<(Guid, DateOnly), int>();
        var slots = new Dictionary<(Guid, DateOnly), CoreShift>();
        var locations = new Dictionary<Guid, string>();

        foreach (var shift in context.Shifts)
        {
            if (!Guid.TryParse(shift.Id, out var shiftRefId) || !DateOnly.TryParse(shift.Date, out var date))
            {
                continue;
            }

            var key = (shiftRefId, date);
            capacities.TryGetValue(key, out var current);
            capacities[key] = current + Math.Max(1, shift.RequiredAssignments);
            slots.TryAdd(key, shift);

            if (shift.LocationContext is { Length: > 0 } location)
            {
                locations.TryAdd(shiftRefId, location);
            }
        }

        return (capacities, slots, locations);
    }
}
