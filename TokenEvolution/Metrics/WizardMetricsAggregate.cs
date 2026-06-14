// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution.Metrics;

/// <summary>
/// Aggregate statistics over N independent runs of the same scenario with different seeds.
/// Used to distinguish real tuning effects from GA stochastic noise.
/// </summary>
/// <param name="RunCount">Number of runs aggregated.</param>
/// <param name="CoverageMean">Mean coverage percent across all runs.</param>
/// <param name="CoverageStdDev">Sample standard deviation of coverage percent.</param>
/// <param name="TargetReachedMean">Mean target-reached fraction across all runs.</param>
/// <param name="TargetReachedStdDev">Sample standard deviation of target-reached fraction.</param>
/// <param name="SlotGiniMean">Mean Gini coefficient.</param>
/// <param name="SlotGiniStdDev">Sample standard deviation of Gini.</param>
/// <param name="ShiftTypeEntropyMean">Mean shift-type entropy.</param>
/// <param name="ShiftTypeEntropyStdDev">Sample standard deviation of entropy.</param>
/// <param name="MaxConsecutiveBlockLenMean">Mean longest consecutive-day block.</param>
/// <param name="MaxConsecutiveBlockLenMax">Worst-case (highest) consecutive block observed across runs.</param>
/// <param name="RosterFidelityInversionMean">Mean roster-fidelity inversion rate (0 = top-down rule fully honoured).</param>
/// <param name="RosterFidelityInversionStdDev">Sample standard deviation of the roster-fidelity inversion rate.</param>
public sealed record WizardMetricsAggregate(
    int RunCount,
    double CoverageMean,
    double CoverageStdDev,
    double TargetReachedMean,
    double TargetReachedStdDev,
    double SlotGiniMean,
    double SlotGiniStdDev,
    double ShiftTypeEntropyMean,
    double ShiftTypeEntropyStdDev,
    double MaxConsecutiveBlockLenMean,
    int MaxConsecutiveBlockLenMax,
    double RosterFidelityInversionMean = 0,
    double RosterFidelityInversionStdDev = 0)
{
    public static WizardMetricsAggregate FromSnapshots(IReadOnlyList<WizardMetricsSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            throw new ArgumentException("at least one snapshot required", nameof(snapshots));
        }
        return new WizardMetricsAggregate(
            RunCount: snapshots.Count,
            CoverageMean: Mean(snapshots, s => s.CoveragePercent),
            CoverageStdDev: StdDev(snapshots, s => s.CoveragePercent),
            TargetReachedMean: Mean(snapshots, s => s.TargetReachedPercent),
            TargetReachedStdDev: StdDev(snapshots, s => s.TargetReachedPercent),
            SlotGiniMean: Mean(snapshots, s => s.SlotGini),
            SlotGiniStdDev: StdDev(snapshots, s => s.SlotGini),
            ShiftTypeEntropyMean: Mean(snapshots, s => s.ShiftTypeEntropyAvg),
            ShiftTypeEntropyStdDev: StdDev(snapshots, s => s.ShiftTypeEntropyAvg),
            MaxConsecutiveBlockLenMean: Mean(snapshots, s => (double)s.MaxConsecutiveBlockLen),
            MaxConsecutiveBlockLenMax: snapshots.Max(s => s.MaxConsecutiveBlockLen),
            RosterFidelityInversionMean: Mean(snapshots, s => s.RosterFidelityInversionRate),
            RosterFidelityInversionStdDev: StdDev(snapshots, s => s.RosterFidelityInversionRate));
    }

    private static double Mean(IReadOnlyList<WizardMetricsSnapshot> snaps, Func<WizardMetricsSnapshot, double> selector)
        => snaps.Average(selector);

    private static double StdDev(IReadOnlyList<WizardMetricsSnapshot> snaps, Func<WizardMetricsSnapshot, double> selector)
    {
        if (snaps.Count < 2)
        {
            return 0.0;
        }
        var mean = snaps.Average(selector);
        var variance = snaps.Sum(s => Math.Pow(selector(s) - mean, 2)) / (snaps.Count - 1);
        return Math.Sqrt(variance);
    }
}
