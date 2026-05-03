// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

/// <summary>
/// Determines the top-down processing order for the conductor. Primary: target hours descending.
/// Tiebreaker: count of cells whose symbol is in the agent's preferred set, descending. Final
/// tiebreaker: agent id (stable, deterministic).
/// </summary>
public static class RowSorter
{
    public static HarmonyBitmap Sort(HarmonyBitmap bitmap)
    {
        var permutation = ComputePermutation(bitmap);
        return bitmap.WithRowOrder(permutation);
    }

    public static IReadOnlyList<int> ComputePermutation(HarmonyBitmap bitmap)
    {
        var keys = new RowKey[bitmap.RowCount];
        for (var r = 0; r < bitmap.RowCount; r++)
        {
            var agent = bitmap.Rows[r];
            keys[r] = new RowKey(r, agent.TargetHours, CountPreferredMatches(bitmap, r, agent), agent.Id);
        }

        Array.Sort(keys, RowKeyComparer.Instance);

        var permutation = new int[bitmap.RowCount];
        for (var i = 0; i < keys.Length; i++)
        {
            permutation[i] = keys[i].OriginalIndex;
        }
        return permutation;
    }

    private static int CountPreferredMatches(HarmonyBitmap bitmap, int rowIndex, BitmapAgent agent)
    {
        if (agent.PreferredShiftSymbols.Count == 0)
        {
            return 0;
        }

        var count = 0;
        foreach (var cell in bitmap.Row(rowIndex))
        {
            if (cell.Symbol != CellSymbol.Free && agent.PreferredShiftSymbols.Contains(cell.Symbol))
            {
                count++;
            }
        }
        return count;
    }

    private readonly record struct RowKey(int OriginalIndex, decimal TargetHours, int PreferredMatches, string AgentId);

    private sealed class RowKeyComparer : IComparer<RowKey>
    {
        public static RowKeyComparer Instance { get; } = new();

        public int Compare(RowKey x, RowKey y)
        {
            var byHours = y.TargetHours.CompareTo(x.TargetHours);
            if (byHours != 0)
            {
                return byHours;
            }

            var byPreferred = y.PreferredMatches.CompareTo(x.PreferredMatches);
            if (byPreferred != 0)
            {
                return byPreferred;
            }

            return string.CompareOrdinal(x.AgentId, y.AgentId);
        }
    }
}
