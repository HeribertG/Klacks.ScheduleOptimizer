// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Evolution;

namespace Klacks.ScheduleOptimizer.Harmonizer.Rules;

/// <summary>
/// Fitness decorator for the soft planning rules: fitness - lambda * SoftPenalty / RowCount, row scores unchanged.
/// It must wrap the memoized harmony evaluator from OUTSIDE: that memo is keyed by one row (agent + cells), while a
/// team-fairness penalty depends on all rows. The soft penalty is decomposed instead: a per-row memo holds the row's
/// per-agent soft penalty and its team-fairness window values (both depend only on agent and cells), the team part is
/// recombined from all rows on every call. In front of that memo sits the last penalty seen per row index: a search
/// changes one or two rows between calls, so an unchanged row is recognised by comparing its cell references without
/// hashing it. Install only when the rule set has a soft rule; without one the caller
/// keeps the undecorated evaluator (byte-identical fitness). Not thread-safe; one instance per run.
/// </summary>
/// <param name="inner">Harmony fitness (memoized or plain)</param>
/// <param name="runtime">Rule state of the run</param>
/// <param name="softPenaltyWeight">lambda: fitness lost per unit of weighted soft excess and row</param>
public sealed class RuleAwareBitmapFitnessEvaluator : IBitmapFitnessEvaluator
{
    /// <summary>
    /// Default lambda: one weighted unit of soft excess costs as much as lowering one row's harmony score by 0.25
    /// (rows are averaged, so the penalty is divided by the row count like a row score). Smallest value of the
    /// stage-4 benchmark sweep (0.05/0.25/1/4) for which the soft penalty never rose on any scenario; 0.05 let it
    /// rise on the 16x37 plan because harmony gains outweighed it.
    /// </summary>
    public const double DefaultSoftPenaltyWeight = 0.25;

    private const int MaxCachedRows = 50_000;

    private readonly IBitmapFitnessEvaluator _inner;
    private readonly BitmapRuleRuntime _runtime;
    private readonly double _softPenaltyWeight;
    private readonly Dictionary<BitmapRowKey, RowPenalty> _rows = new(BitmapRowKey.Comparer);
    private readonly BitmapRowKeyBuffer _probe = new();
    private RowPenalty?[] _lastPenaltyByRow = [];
    private BitmapAgent?[] _lastAgentByRow = [];
    private Cell[]?[] _lastCellsByRow = [];
    private readonly decimal[][] _windowValuesByAgent;

    public RuleAwareBitmapFitnessEvaluator(IBitmapFitnessEvaluator inner, BitmapRuleRuntime runtime, double softPenaltyWeight = DefaultSoftPenaltyWeight)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(runtime);
        if (softPenaltyWeight < 0 || double.IsNaN(softPenaltyWeight))
        {
            throw new ArgumentOutOfRangeException(nameof(softPenaltyWeight), "The soft penalty weight must not be negative.");
        }

        _inner = inner;
        _runtime = runtime;
        _softPenaltyWeight = softPenaltyWeight;
        var agentCount = runtime.Projection.Context.AgentCount;
        _windowValuesByAgent = new decimal[agentCount][];
        for (var i = 0; i < agentCount; i++)
        {
            _windowValuesByAgent[i] = new decimal[runtime.Evaluator.TeamWindowCount];
        }
    }

    public IBitmapFitnessEvaluator Inner => _inner;

    public FitnessResult Evaluate(HarmonyBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var result = _inner.Evaluate(bitmap);
        if (bitmap.RowCount == 0)
        {
            return result;
        }

        return result with { Fitness = result.Fitness - (_softPenaltyWeight * SoftPenalty(bitmap) / bitmap.RowCount) };
    }

    /// <summary>Weighted soft excess of the bitmap (per-agent soft rules plus team fairness).</summary>
    public double SoftPenalty(HarmonyBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var projection = _runtime.Projection;
        var penalty = 0d;
        for (var row = 0; row < bitmap.RowCount; row++)
        {
            if (!TryLastOfRow(bitmap, row, out var rowPenalty))
            {
                var agentIndex = projection.AgentIndexOf(bitmap.Rows[row]);
                if (agentIndex < 0)
                {
                    continue;
                }

                rowPenalty = MemoizedRowPenalty(bitmap, row, agentIndex);
                RememberRow(bitmap, row, rowPenalty);
            }

            _windowValuesByAgent[rowPenalty.AgentIndex] = rowPenalty.WindowValues;
            penalty += rowPenalty.AgentPenalty;
        }

        return penalty + _runtime.Evaluator.TeamSoftPenalty(_windowValuesByAgent);
    }

    private bool TryLastOfRow(HarmonyBitmap bitmap, int row, out RowPenalty last)
    {
        last = null!;
        if (_lastPenaltyByRow.Length != bitmap.RowCount || _lastPenaltyByRow[row] is not { } remembered
            || !ReferenceEquals(_lastAgentByRow[row], bitmap.Rows[row]))
        {
            return false;
        }

        if (_lastCellsByRow[row] is not { } cells || cells.Length != bitmap.DayCount)
        {
            return false;
        }

        for (var day = 0; day < cells.Length; day++)
        {
            if (!ReferenceEquals(cells[day], bitmap.GetCell(row, day)))
            {
                return false;
            }
        }

        last = remembered;
        return true;
    }

    private void RememberRow(HarmonyBitmap bitmap, int row, RowPenalty rowPenalty)
    {
        if (_lastPenaltyByRow.Length != bitmap.RowCount)
        {
            _lastPenaltyByRow = new RowPenalty?[bitmap.RowCount];
            _lastAgentByRow = new BitmapAgent?[bitmap.RowCount];
            _lastCellsByRow = new Cell[]?[bitmap.RowCount];
        }

        var cells = _lastCellsByRow[row];
        if (cells is null || cells.Length != bitmap.DayCount)
        {
            cells = new Cell[bitmap.DayCount];
            _lastCellsByRow[row] = cells;
        }

        for (var day = 0; day < cells.Length; day++)
        {
            cells[day] = bitmap.GetCell(row, day);
        }

        _lastAgentByRow[row] = bitmap.Rows[row];
        _lastPenaltyByRow[row] = rowPenalty;
    }

    private RowPenalty MemoizedRowPenalty(HarmonyBitmap bitmap, int row, int agentIndex)
    {
        var key = _probe.Fill(bitmap, row);
        if (_rows.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var plan = _runtime.ProjectIntoScratch(bitmap, row, agentIndex);
        var values = new decimal[_runtime.Evaluator.TeamWindowCount];
        var rowPenalty = new RowPenalty(agentIndex, _runtime.Evaluator.AgentSoftPenalty(plan, agentIndex, values), values);
        if (_rows.Count >= MaxCachedRows)
        {
            _rows.Clear();
        }

        _rows[key.ToOwned()] = rowPenalty;
        return rowPenalty;
    }

    private sealed record RowPenalty(int AgentIndex, double AgentPenalty, decimal[] WindowValues);
}
