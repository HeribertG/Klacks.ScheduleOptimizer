// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// Candidate census of one <see cref="RotationBalancer"/> invocation: how many same-day block pairs survived each
/// acceptance stage, plus detail lines for the first pairs the lexicographic comparison refused. Only built when a
/// diagnostics sink listens.
/// </summary>
internal sealed class RotationBalanceCensus
{
    private const int MaxDetailLines = 12;

    internal enum Stage
    {
        SameDayPairs,
        PassedSlotFilter,
        BetterRotation,
        HigherBlockOrder,
        Accepted,
    }

    private readonly int[] _counts = new int[Enum.GetValues<Stage>().Length];
    private readonly List<string> _details = [];

    public void Count(Stage stage) => _counts[(int)stage]++;

    public void Reject(string line)
    {
        if (_details.Count < MaxDetailLines)
        {
            _details.Add(line);
        }
    }

    public void Report(Action<string> sink)
    {
        sink("ROTATION-BALANCE " + string.Join(" ", Enum.GetValues<Stage>().Select(s => $"{s}={_counts[(int)s]}")));
        foreach (var line in _details)
        {
            sink($"ROTATION-BALANCE compareRejected {line}");
        }
    }
}
