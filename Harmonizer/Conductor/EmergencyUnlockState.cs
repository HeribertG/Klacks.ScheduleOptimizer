// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Conductor;

/// <summary>
/// Per-row tracking of whether an emergency unlock has already been spent. Each row may
/// trigger an emergency unlock at most once during the entire harmonisation run.
/// </summary>
public sealed class EmergencyUnlockState
{
    private readonly bool[] _used;

    public EmergencyUnlockState(int rowCount)
    {
        _used = new bool[rowCount];
    }

    public bool IsUsed(int rowIndex) => _used[rowIndex];

    public void MarkUsed(int rowIndex) => _used[rowIndex] = true;
}
