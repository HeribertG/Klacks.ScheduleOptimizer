// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Order-loyalty counters of a whole plan: the denominators a swap cannot change and the switch
/// counts it can.
/// </summary>
/// <param name="WithinPairs">Consecutive assignment pairs inside a package</param>
/// <param name="WithinSwitches">Order switches among them</param>
/// <param name="AcrossPairs">Assignment pairs spanning a package boundary</param>
/// <param name="AcrossSwitches">Order switches among them</param>

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

internal readonly record struct PairTotals(
    int WithinPairs,
    int WithinSwitches,
    int AcrossPairs,
    int AcrossSwitches);
