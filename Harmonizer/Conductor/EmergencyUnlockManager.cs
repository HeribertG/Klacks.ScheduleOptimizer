// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Conductor;

/// <summary>
/// Decides whether a row may break the lock on an already-processed upper row. Eligible
/// rows are those whose harmony score sits below a configurable fraction of the median
/// row score and that have not yet triggered an emergency unlock.
/// </summary>
public sealed class EmergencyUnlockManager
{
    private readonly EmergencyUnlockState _state;
    private readonly double _scoreFractionOfMedianThreshold;

    public EmergencyUnlockManager(EmergencyUnlockState state, double scoreFractionOfMedianThreshold = 0.5)
    {
        if (scoreFractionOfMedianThreshold < 0 || scoreFractionOfMedianThreshold > 1)
        {
            throw new ArgumentException("Threshold must be in [0,1].", nameof(scoreFractionOfMedianThreshold));
        }
        _state = state;
        _scoreFractionOfMedianThreshold = scoreFractionOfMedianThreshold;
    }

    public bool CanUnlock(int rowIndex, double rowScore, double medianScore)
    {
        if (_state.IsUsed(rowIndex))
        {
            return false;
        }
        return rowScore < medianScore * _scoreFractionOfMedianThreshold;
    }

    public void MarkUsed(int rowIndex) => _state.MarkUsed(rowIndex);

    public double Threshold => _scoreFractionOfMedianThreshold;
}
