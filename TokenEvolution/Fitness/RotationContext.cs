// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using Klacks.ScheduleOptimizer.Common.Rotation;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

/// <summary>
/// Wizard 1's inputs to <see cref="ShiftRotation"/> beyond the plan itself. First, the agent's worked shifts before the
/// period (boundary locked works and existing work blockers), so a block change across the seam is judged against
/// the carry-in block (SPEC-ROTATION-2026-10-08 rule 6). Second, the answer to "may the agent work this kind on this day": a kind is closed
/// on a day for a plan-independent hard reason only (SPEC-ROTATION-2026-10-08 rule 5) — no shift work (only early stays
/// open, as the engine's veto has it; owner question G1 is open), a day directive, or every slot of the kind on that
/// day being ineligible or blacklisted for the agent. A day without a slot of the kind is "no demand", not closed.
/// Known gap: <see cref="CoreWizardContext.IneligibleAssignments"/> also carries availability ineligibility, which the
/// spec does not list as a reason; expired qualifications only count while their blocking setting is on.
/// Built once per context and cached, because the fitness reads it for every evaluated scenario.
/// </summary>
/// <param name="context">The run's wizard context</param>
public sealed class RotationContext
{
    private static readonly ConditionalWeakTable<CoreWizardContext, RotationContext> Cache = new();

    private readonly CoreWizardContext _context;
    private readonly HashSet<string> _noShiftWork;
    private readonly Dictionary<(int Kind, DateOnly Date), List<Guid>> _slotsByKindAndDate = [];
    private readonly Dictionary<(string AgentId, DateOnly Date), List<CoreScheduleCommand>> _commands = [];
    private readonly Dictionary<string, List<CoreShiftPreference>> _preferences = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(DateOnly Date, int KindIndex, DateTime StartAt, DateTime EndAt)>> _boundaryShifts =
        new(StringComparer.Ordinal);

    private RotationContext(CoreWizardContext context)
    {
        _context = context;

        foreach (var work in context.BoundaryLockedWorks.Where(w => w.Date < context.PeriodFrom))
        {
            AddBoundaryShift(work.AgentId, (work.Date, work.ShiftTypeIndex, work.StartAt, work.EndAt));
        }

        foreach (var work in context.BoundaryExistingWorkBlockers.Where(w => w.Date < context.PeriodFrom))
        {
            var kind = ShiftTypeInference.FromSpan(TimeOnly.FromDateTime(work.StartAt), TimeOnly.FromDateTime(work.EndAt));
            AddBoundaryShift(work.AgentId, (work.Date, kind, work.StartAt, work.EndAt));
        }
        _noShiftWork = context.Agents.Where(a => !a.PerformsShiftWork).Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var shift in context.Shifts)
        {
            if (!Guid.TryParse(shift.Id, out var shiftId) || !DateOnly.TryParse(shift.Date, out var date))
            {
                continue;
            }

            var key = (ShiftTypeInference.FromSpanString(shift.StartTime, shift.EndTime), date);
            if (!_slotsByKindAndDate.TryGetValue(key, out var ids))
            {
                _slotsByKindAndDate[key] = ids = [];
            }

            ids.Add(shiftId);
        }

        foreach (var command in context.ScheduleCommands)
        {
            var key = (command.AgentId, command.Date);
            if (!_commands.TryGetValue(key, out var list))
            {
                _commands[key] = list = [];
            }

            list.Add(command);
        }

        foreach (var preference in context.ShiftPreferences.Where(p => p.Kind == ShiftPreferenceKind.Blacklist))
        {
            if (!_preferences.TryGetValue(preference.AgentId, out var list))
            {
                _preferences[preference.AgentId] = list = [];
            }

            list.Add(preference);
        }
    }

    public static RotationContext For(CoreWizardContext context) => Cache.GetValue(context, c => new RotationContext(c));

    /// <summary>Worked shifts of the agent before the period; empty without boundary works.</summary>
    public IReadOnlyList<(DateOnly Date, int KindIndex, DateTime StartAt, DateTime EndAt)> BoundaryShiftsOf(string agentId)
        => _boundaryShifts.TryGetValue(agentId, out var list) ? list : [];

    private void AddBoundaryShift(string agentId, (DateOnly Date, int KindIndex, DateTime StartAt, DateTime EndAt) shift)
    {
        if (!_boundaryShifts.TryGetValue(agentId, out var list))
        {
            _boundaryShifts[agentId] = list = [];
        }

        list.Add(shift);
    }

    /// <summary>True when the agent may work the kind on at least one of the days.</summary>
    public bool IsAllowedOnAnyDay(string agentId, int kindIndex, IReadOnlyList<DateOnly> days)
    {
        foreach (var day in days)
        {
            if (!IsClosed(agentId, kindIndex, day))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsClosed(string agentId, int kindIndex, DateOnly date)
    {
        if (_noShiftWork.Contains(agentId) && kindIndex != ShiftRotation.EarlyKindIndex)
        {
            return true;
        }

        if (_commands.TryGetValue((agentId, date), out var commands)
            && !SlotConstraintFilter.RespectsKeyword(agentId, date, kindIndex, commands))
        {
            return true;
        }

        if (!_slotsByKindAndDate.TryGetValue((kindIndex, date), out var slots))
        {
            return false;
        }

        var blacklist = _preferences.TryGetValue(agentId, out var list) ? list : [];
        foreach (var shiftId in slots)
        {
            if (_context.IsEligible(agentId, shiftId, date)
                && !SlotConstraintFilter.IsBlacklistedShift(agentId, shiftId, blacklist))
            {
                return false;
            }
        }

        return true;
    }
}
