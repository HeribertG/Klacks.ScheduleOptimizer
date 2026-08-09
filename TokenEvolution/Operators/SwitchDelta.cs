// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Change in order switches a planned swap would cause, split by the two dimensions of rule 10.
/// </summary>
/// <param name="Within">Change in the number of order switches inside a package</param>
/// <param name="Across">Change in the number of order switches across a package boundary</param>

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

internal readonly record struct SwitchDelta(int Within, int Across)
{
    public static SwitchDelta operator +(SwitchDelta left, SwitchDelta right)
        => new(left.Within + right.Within, left.Across + right.Across);

    public static SwitchDelta Add(SwitchDelta left, SwitchDelta right) => left + right;
}
