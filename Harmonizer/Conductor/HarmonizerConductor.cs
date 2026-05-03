// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Scorer;

namespace Klacks.ScheduleOptimizer.Harmonizer.Conductor;

/// <summary>
/// Top-down row processor. Each row is locked after processing; locked rows are off-limits
/// to subsequent moves except via the EmergencyUnlockManager. Rows whose AgentId appears in
/// the optional softening-hint set get an enlarged iteration budget so the conductor spends
/// more effort on slots that Wizard 1 already had to soften — exactly the cells where harmony
/// gains are most valuable.
/// </summary>
public sealed class HarmonizerConductor
{
    private readonly HarmonyScorer _scorer;
    private readonly ReplaceMutation _mutation;
    private readonly EmergencyUnlockManager _emergencyUnlock;
    private readonly int _maxIterationsPerRow;
    private readonly int _hintRowIterationMultiplier;
    private readonly IReadOnlySet<string> _hintAgentIds;

    public HarmonizerConductor(
        HarmonyScorer scorer,
        ReplaceMutation mutation,
        EmergencyUnlockManager emergencyUnlock,
        int maxIterationsPerRow = 16,
        IReadOnlyList<SofteningHint>? hints = null,
        int hintRowIterationMultiplier = 2)
    {
        _scorer = scorer;
        _mutation = mutation;
        _emergencyUnlock = emergencyUnlock;
        _maxIterationsPerRow = maxIterationsPerRow;
        _hintRowIterationMultiplier = hintRowIterationMultiplier <= 0 ? 1 : hintRowIterationMultiplier;
        _hintAgentIds = hints is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(hints.Select(h => h.AgentId), StringComparer.Ordinal);
    }

    public ConductorResult Run(HarmonyBitmap bitmap)
    {
        var lockedRows = new HashSet<int>();
        var rowTraces = new List<RowTrace>(bitmap.RowCount);
        var initialScores = ComputeAllRowScores(bitmap);
        var medianInitial = Median(initialScores);

        for (var rowIndex = 0; rowIndex < bitmap.RowCount; rowIndex++)
        {
            var trace = ProcessRow(bitmap, rowIndex, lockedRows, initialScores[rowIndex], medianInitial);
            rowTraces.Add(trace);
            lockedRows.Add(rowIndex);
        }

        var finalScores = ComputeAllRowScores(bitmap);
        return new ConductorResult(rowTraces, initialScores, finalScores);
    }

    private RowTrace ProcessRow(
        HarmonyBitmap bitmap,
        int rowIndex,
        HashSet<int> lockedRows,
        double scoreBefore,
        double medianInitialScore)
    {
        var movesApplied = 0;
        var emergencyUsed = false;
        var iterationBudget = _hintAgentIds.Contains(bitmap.Rows[rowIndex].Id)
            ? _maxIterationsPerRow * _hintRowIterationMultiplier
            : _maxIterationsPerRow;

        for (var iteration = 0; iteration < iterationBudget; iteration++)
        {
            var outcome = _mutation.FindBestMove(bitmap, rowIndex, lockedRows);
            if (outcome.Move is not null)
            {
                _mutation.Apply(bitmap, outcome.Move);
                movesApplied++;
                continue;
            }

            if (emergencyUsed)
            {
                break;
            }

            var rowScore = _scorer.Score(bitmap, rowIndex).Score;
            if (!_emergencyUnlock.CanUnlock(rowIndex, rowScore, medianInitialScore))
            {
                break;
            }

            var unlockedOutcome = _mutation.FindBestMove(bitmap, rowIndex, EmptySet);
            if (unlockedOutcome.Move is null)
            {
                break;
            }

            _mutation.Apply(bitmap, unlockedOutcome.Move);
            _emergencyUnlock.MarkUsed(rowIndex);
            emergencyUsed = true;
            movesApplied++;
        }

        var scoreAfter = _scorer.Score(bitmap, rowIndex).Score;
        return new RowTrace(rowIndex, scoreBefore, scoreAfter, movesApplied, emergencyUsed);
    }

    private double[] ComputeAllRowScores(HarmonyBitmap bitmap)
    {
        var scores = new double[bitmap.RowCount];
        for (var r = 0; r < bitmap.RowCount; r++)
        {
            scores[r] = _scorer.Score(bitmap, r).Score;
        }
        return scores;
    }

    private static double Median(double[] values)
    {
        if (values.Length == 0)
        {
            return 0;
        }
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    private static readonly HashSet<int> EmptySet = [];
}

/// <param name="RowIndex">Index of the row in processing order</param>
/// <param name="ScoreBefore">Harmony score before processing</param>
/// <param name="ScoreAfter">Harmony score after processing</param>
/// <param name="MovesApplied">Number of accepted swaps during processing</param>
/// <param name="EmergencyUnlockTriggered">True if the row consumed its emergency unlock</param>
public sealed record RowTrace(
    int RowIndex,
    double ScoreBefore,
    double ScoreAfter,
    int MovesApplied,
    bool EmergencyUnlockTriggered);

/// <param name="RowTraces">Per-row processing trace</param>
/// <param name="InitialRowScores">Row scores before any processing started</param>
/// <param name="FinalRowScores">Row scores after the conductor finished</param>
public sealed record ConductorResult(
    IReadOnlyList<RowTrace> RowTraces,
    IReadOnlyList<double> InitialRowScores,
    IReadOnlyList<double> FinalRowScores);
