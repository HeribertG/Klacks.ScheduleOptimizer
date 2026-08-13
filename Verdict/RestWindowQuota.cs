// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Verdict;

/// <summary>
/// A period-scaled rest-window debt of the final verdict: the plan owes every scheduled agent a
/// number of free windows of a given size, and that number grows and shrinks with the period —
/// it is a function, never a constant (owner model of 2026-08-13: four 48-hour windows per month,
/// three 72-hour windows, both halved over a fortnight).
/// </summary>
/// <param name="WindowHours">Size of one rest window in hours (e.g. 48)</param>
/// <param name="WindowsPerReferencePeriod">Windows owed per reference period (e.g. 4 per 28 days)</param>
public sealed record RestWindowQuota(double WindowHours, double WindowsPerReferencePeriod)
{
    /// <summary>Windows owed for a concrete period, linearly scaled from the reference period.</summary>
    /// <param name="periodDays">Length of the judged period in days</param>
    /// <param name="referencePeriodDays">Length of the reference period the quota is stated for</param>
    public double RequiredWindows(int periodDays, int referencePeriodDays)
        => WindowsPerReferencePeriod * periodDays / referencePeriodDays;
}
