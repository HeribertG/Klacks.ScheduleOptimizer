// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Auction.Agent;

/// <summary>
/// Per-agent transient state used by the bidding agent to compute scores.
/// Lives only within a single auction run; never persisted between GA generations.
/// </summary>
/// <param name="AgentId">Agent identifier</param>
/// <param name="HoursAssignedThisRun">Hours the auction has assigned so far</param>
/// <param name="CurrentBlockLength">Length of the in-progress consecutive work block (0 if last day was rest)</param>
/// <param name="LastWorkedDate">Most recent assigned date or null</param>
/// <param name="DaysSinceShiftType">Index 0=early, 1=late, 2=night → days since last shift of that type (int.MaxValue if never)</param>
/// <param name="CurrentBlockStartShiftType">Shift type the in-progress block started with (-1 = no block yet); used by the rotation rule when the next block begins</param>
public sealed record AgentRuntimeState(
    string AgentId,
    double HoursAssignedThisRun,
    int CurrentBlockLength,
    DateOnly? LastWorkedDate,
    IReadOnlyList<int> DaysSinceShiftType,
    int CurrentBlockStartShiftType = -1)
{
    public static AgentRuntimeState Initial(string agentId) =>
        new(agentId, 0.0, 0, null, [int.MaxValue, int.MaxValue, int.MaxValue]);

    /// <summary>
    /// Seeds the runtime state from boundary works (locked + existing) before the period start so
    /// the first in-period assignment continues an existing block and the rotation rules see how
    /// recently the agent worked each shift type.
    /// </summary>
    public static AgentRuntimeState InitialFromBoundary(
        string agentId,
        DateOnly periodFrom,
        IReadOnlyList<CoreLockedWork> boundaryLocked,
        IReadOnlyList<CoreExistingWorkBlocker> boundaryExisting)
    {
        DateOnly? lastWorkedDate = null;
        var lastWorkedType = -1;
        var daysSince = new int[] { int.MaxValue, int.MaxValue, int.MaxValue };

        foreach (var locked in boundaryLocked)
        {
            if (locked.AgentId != agentId || locked.Date >= periodFrom)
            {
                continue;
            }
            if (!lastWorkedDate.HasValue || locked.Date > lastWorkedDate.Value)
            {
                lastWorkedDate = locked.Date;
                lastWorkedType = locked.ShiftTypeIndex;
            }
            UpdateDaysSince(daysSince, locked.ShiftTypeIndex, periodFrom, locked.Date);
        }

        foreach (var blocker in boundaryExisting)
        {
            if (blocker.AgentId != agentId || blocker.Date >= periodFrom)
            {
                continue;
            }
            var idx = ShiftTypeInference.FromStartTime(TimeOnly.FromDateTime(blocker.StartAt));
            if (!lastWorkedDate.HasValue || blocker.Date > lastWorkedDate.Value)
            {
                lastWorkedDate = blocker.Date;
                lastWorkedType = idx;
            }
            UpdateDaysSince(daysSince, idx, periodFrom, blocker.Date);
        }

        var currentBlockLength = 0;
        if (lastWorkedDate.HasValue)
        {
            var probe = periodFrom.AddDays(-1);
            while (HasBoundaryWorkOnDate(agentId, probe, boundaryLocked, boundaryExisting))
            {
                currentBlockLength++;
                probe = probe.AddDays(-1);
            }
        }

        // The most recent boundary work's type approximates the trailing block's start type —
        // blocks are homogeneous in practice and only the rotation rule consumes this value.
        var currentBlockStartType = currentBlockLength > 0 ? lastWorkedType : -1;

        return new AgentRuntimeState(agentId, 0.0, currentBlockLength, lastWorkedDate, daysSince, currentBlockStartType);
    }

    private static void UpdateDaysSince(int[] daysSince, int shiftTypeIndex, DateOnly periodFrom, DateOnly workDate)
    {
        if (shiftTypeIndex < 0 || shiftTypeIndex >= daysSince.Length)
        {
            return;
        }
        var gap = periodFrom.DayNumber - workDate.DayNumber;
        if (daysSince[shiftTypeIndex] == int.MaxValue || gap < daysSince[shiftTypeIndex])
        {
            daysSince[shiftTypeIndex] = gap;
        }
    }

    private static bool HasBoundaryWorkOnDate(
        string agentId,
        DateOnly date,
        IReadOnlyList<CoreLockedWork> locked,
        IReadOnlyList<CoreExistingWorkBlocker> existing)
    {
        foreach (var l in locked)
        {
            if (l.AgentId == agentId && OccupiesDate(l.StartAt, l.EndAt, date))
            {
                return true;
            }
        }
        foreach (var e in existing)
        {
            if (e.AgentId == agentId && OccupiesDate(e.StartAt, e.EndAt, date))
            {
                return true;
            }
        }
        return false;
    }

    private static bool OccupiesDate(DateTime startAt, DateTime endAt, DateOnly target)
    {
        var dayStart = target.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        return startAt < dayEnd && endAt > dayStart;
    }
}
