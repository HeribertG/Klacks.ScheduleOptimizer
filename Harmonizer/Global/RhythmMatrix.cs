// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Harmonizer.Global;

/// <summary>
/// Canon measure of a plan. For every pair of rows it stores the best similarity of one row to the other row shifted
/// cyclically by any number of days; the rhythm is the mean fit of all working rows to the row that explains them best
/// (the template). Absence cells (Break) are wildcards: an absence never breaks the rhythm. Rows without any worked day
/// take no part. The matrix is symmetric and can be refreshed for a few changed rows, so a search that rebuilds a small
/// region pays per changed row, not per plan.
/// </summary>
public sealed class RhythmMatrix
{
    /// <summary>Symbol value that matches every other symbol (used for absences).</summary>
    public const byte Wildcard = byte.MaxValue;

    private readonly int _rows;
    private readonly int _days;
    private readonly byte[][] _symbols;
    private readonly bool[] _working;
    private readonly double[,] _pair;

    public RhythmMatrix(HarmonyBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        _rows = bitmap.RowCount;
        _days = bitmap.DayCount;
        _symbols = new byte[_rows][];
        _working = new bool[_rows];
        _pair = new double[_rows, _rows];
        for (var r = 0; r < _rows; r++)
        {
            _symbols[r] = new byte[_days];
            LoadRow(bitmap, r);
        }

        FillPairs();
    }

    /// <summary>
    /// Builds the matrix from raw rows of equal length: one byte per day holding the <see cref="CellSymbol"/> value,
    /// <see cref="Wildcard"/> for an absence. Used by Wizard 1, whose plans are tokens rather than a bitmap.
    /// </summary>
    public RhythmMatrix(IReadOnlyList<byte[]> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows = rows.Count;
        _days = _rows == 0 ? 0 : rows[0].Length;
        _symbols = new byte[_rows][];
        _working = new bool[_rows];
        _pair = new double[_rows, _rows];
        for (var r = 0; r < _rows; r++)
        {
            if (rows[r].Length != _days)
            {
                throw new ArgumentException("All rows must have the same length.", nameof(rows));
            }

            _symbols[r] = (byte[])rows[r].Clone();
            _working[r] = _symbols[r].Any(IsWorkSymbol);
        }

        FillPairs();
    }

    private RhythmMatrix(RhythmMatrix source)
    {
        _rows = source._rows;
        _days = source._days;
        _symbols = source._symbols.Select(row => (byte[])row.Clone()).ToArray();
        _working = (bool[])source._working.Clone();
        _pair = (double[,])source._pair.Clone();
    }

    /// <summary>Independent copy, so a search can try a change and throw it away.</summary>
    public RhythmMatrix Clone() => new(this);

    /// <summary>Mean fit of the working rows to the best template row; 1 when fewer than two rows work.</summary>
    public double Rhythm => BestTemplate().Mean;

    /// <summary>Fit of one row to the current best template (1 for a row that does not work).</summary>
    public double RowFit(int row)
    {
        if (!_working[row])
        {
            return 1d;
        }

        var (template, _) = BestTemplate();
        return template < 0 ? 1d : _pair[row, template];
    }

    /// <summary>Row that currently explains the working rows best (the canon template), -1 when fewer than two rows work.</summary>
    public int TemplateRow => BestTemplate().Template;

    /// <summary>
    /// Symbol the canon expects for <paramref name="row"/> on every day: the template row read at the cyclic shift that
    /// matches this row best (the alignment <see cref="RowFit"/> is measured at). <see cref="Wildcard"/> where the
    /// template has an absence; null when the row does not work or there is no template.
    /// </summary>
    public byte[]? ExpectedRow(int row)
    {
        var template = BestTemplate().Template;
        if (template < 0 || !_working[row])
        {
            return null;
        }

        var shift = BestShift(_symbols[row], _symbols[template]);
        var expected = new byte[_days];
        for (var d = 0; d < _days; d++)
        {
            expected[d] = _symbols[template][(d + shift) % _days];
        }

        return expected;
    }

    /// <summary>Reloads the given rows from the bitmap and recomputes their pairs.</summary>
    public void Refresh(HarmonyBitmap bitmap, IEnumerable<int> rows)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        foreach (var r in rows.Distinct())
        {
            LoadRow(bitmap, r);
            for (var other = 0; other < _rows; other++)
            {
                SetPair(r, other);
            }
        }
    }

    private (int Template, double Mean) BestTemplate()
    {
        var workingCount = 0;
        for (var r = 0; r < _rows; r++)
        {
            if (_working[r])
            {
                workingCount++;
            }
        }

        if (workingCount < 2)
        {
            return (-1, 1d);
        }

        var bestTemplate = -1;
        var bestMean = double.NegativeInfinity;
        for (var t = 0; t < _rows; t++)
        {
            if (!_working[t])
            {
                continue;
            }

            var sum = 0d;
            for (var r = 0; r < _rows; r++)
            {
                if (_working[r])
                {
                    sum += _pair[r, t];
                }
            }

            var mean = sum / workingCount;
            if (mean > bestMean)
            {
                bestMean = mean;
                bestTemplate = t;
            }
        }

        return (bestTemplate, bestMean);
    }

    private static bool IsWorkSymbol(byte symbol)
        => symbol is (byte)CellSymbol.Early or (byte)CellSymbol.Late or (byte)CellSymbol.Night or (byte)CellSymbol.Other;

    private void FillPairs()
    {
        for (var a = 0; a < _rows; a++)
        {
            for (var b = a; b < _rows; b++)
            {
                SetPair(a, b);
            }
        }
    }

    private void LoadRow(HarmonyBitmap bitmap, int row)
    {
        var working = false;
        for (var d = 0; d < _days; d++)
        {
            var symbol = bitmap.GetCell(row, d).Symbol;
            _symbols[row][d] = symbol == CellSymbol.Break ? Wildcard : (byte)symbol;
            working |= symbol is CellSymbol.Early or CellSymbol.Late or CellSymbol.Night or CellSymbol.Other;
        }

        _working[row] = working;
    }

    private void SetPair(int a, int b)
    {
        var value = a == b ? 1d : BestShiftSimilarity(_symbols[a], _symbols[b]);
        _pair[a, b] = value;
        _pair[b, a] = value;
    }

    private int BestShift(byte[] a, byte[] b)
    {
        var best = -1d;
        var bestShift = 0;
        for (var shift = 0; shift < _days; shift++)
        {
            var value = Similarity(a, b, shift);
            if (value > best)
            {
                best = value;
                bestShift = shift;
            }
        }

        return bestShift;
    }

    private double Similarity(byte[] a, byte[] b, int shift)
    {
        var comparable = 0;
        var matches = 0;
        for (var d = 0; d < _days; d++)
        {
            var x = a[d];
            var y = b[(d + shift) % _days];
            if (x == Wildcard || y == Wildcard)
            {
                continue;
            }

            comparable++;
            if (x == y)
            {
                matches++;
            }
        }

        return comparable == 0 ? 0d : (double)matches / comparable;
    }

    private double BestShiftSimilarity(byte[] a, byte[] b)
    {
        var best = 0d;
        for (var shift = 0; shift < _days; shift++)
        {
            var comparable = 0;
            var matches = 0;
            for (var d = 0; d < _days; d++)
            {
                var x = a[d];
                var y = b[(d + shift) % _days];
                if (x == Wildcard || y == Wildcard)
                {
                    continue;
                }

                comparable++;
                if (x == y)
                {
                    matches++;
                }
            }

            if (comparable > 0)
            {
                best = Math.Max(best, (double)matches / comparable);
            }
        }

        return best;
    }
}
