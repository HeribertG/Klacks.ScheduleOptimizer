// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Scorer;

namespace Klacks.ScheduleOptimizer.Harmonizer.Evolution;

/// <summary>
/// Drop-in replacement for <see cref="HarmonyFitnessEvaluator"/> that caches each row score. A row score
/// only depends on the row's agent and its cells; cells are immutable records that swaps move by reference,
/// so the memo key is the agent reference plus the sequence of cell references of the row (a strict superset
/// of what <see cref="RowFeatureExtractor"/> reads). Results are bit-identical to the uncached evaluator.
/// Not thread-safe; one instance per run.
/// </summary>
/// <param name="scorer">Row scorer used on a cache miss.</param>
/// <param name="maxEntries">Upper bound of cached rows; the cache is cleared when it is exceeded.</param>
public sealed class MemoizedHarmonyFitnessEvaluator : IBitmapFitnessEvaluator
{
    public const int DefaultMaxEntries = 50_000;

    private readonly HarmonyScorer _scorer;
    private readonly int _maxEntries;
    private readonly Dictionary<RowKey, double> _cache = new(RowKeyComparer.Instance);

    public MemoizedHarmonyFitnessEvaluator(HarmonyScorer scorer, int maxEntries = DefaultMaxEntries)
    {
        ArgumentNullException.ThrowIfNull(scorer);
        if (maxEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries), "Cache size must be positive.");
        }
        _scorer = scorer;
        _maxEntries = maxEntries;
    }

    public long Hits { get; private set; }

    public long Misses { get; private set; }

    public FitnessResult Evaluate(HarmonyBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (bitmap.RowCount == 0)
        {
            return new FitnessResult(1.0, []);
        }

        var rowScores = new double[bitmap.RowCount];
        for (var r = 0; r < bitmap.RowCount; r++)
        {
            rowScores[r] = ScoreRow(bitmap, r);
        }
        return new FitnessResult(HarmonyFitnessEvaluator.WeightedFitness(rowScores), rowScores);
    }

    private double ScoreRow(HarmonyBitmap bitmap, int row)
    {
        var cells = new Cell[bitmap.DayCount];
        for (var d = 0; d < cells.Length; d++)
        {
            cells[d] = bitmap.GetCell(row, d);
        }
        var key = new RowKey(bitmap.Rows[row], cells);
        if (_cache.TryGetValue(key, out var cached))
        {
            Hits++;
            return cached;
        }

        Misses++;
        var score = _scorer.Score(bitmap, row).Score;
        if (_cache.Count >= _maxEntries)
        {
            _cache.Clear();
        }
        _cache[key] = score;
        return score;
    }

    private readonly record struct RowKey(BitmapAgent Agent, Cell[] Cells);

    private sealed class RowKeyComparer : IEqualityComparer<RowKey>
    {
        public static readonly RowKeyComparer Instance = new();

        public bool Equals(RowKey x, RowKey y)
        {
            if (!ReferenceEquals(x.Agent, y.Agent) || x.Cells.Length != y.Cells.Length)
            {
                return false;
            }
            for (var i = 0; i < x.Cells.Length; i++)
            {
                if (!ReferenceEquals(x.Cells[i], y.Cells[i]))
                {
                    return false;
                }
            }
            return true;
        }

        public int GetHashCode(RowKey key)
        {
            var hash = new HashCode();
            hash.Add(RuntimeHelpers.GetHashCode(key.Agent));
            foreach (var cell in key.Cells)
            {
                hash.Add(RuntimeHelpers.GetHashCode(cell));
            }
            return hash.ToHashCode();
        }
    }
}
