// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Evolution;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Candidates;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Llm;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Loop;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Validation;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Search;

/// <summary>
/// Deterministic stage-3 optimizer (no LLM): best-improvement local search over the
/// <see cref="MoveCandidatePool"/> neighbourhood. Per iteration it evaluates every single candidate of all three
/// intents and every pair among the best <see cref="DeterministicSearchOptions.PairPoolCap"/> singles through the
/// same <see cref="BatchEvaluator"/> the LLM path uses (hard validator + committee + score-greedy), then applies
/// the best batch. A batch is only applied when the evaluator accepts it, so the score never drops; equal-score
/// (sideways) moves are allowed and the <see cref="TabuList"/> keeps them from being undone (aspiration: a tabu
/// move is still taken when it strictly improves, because after other moves the same coordinates are no longer
/// a reversal); sideways moves
/// after the last strict improvement are rolled back at the end, so every kept batch leads to a gain.
/// Equally good moves send the search into different local optima, so the search runs
/// <see cref="DeterministicSearchOptions.Restarts"/> passes from the same start plan: pass 0 takes the first best
/// move in candidate order, pass k takes a tie by <c>Random(Seed + k)</c>; the best pass wins (earliest on a tie).
/// With <see cref="DeterministicSearchOptions.AlternateNeighbourhoods"/> the odd passes add every hard-valid
/// same-day swap to the pool neighbourhood and passes 0 and 1 both take the first best move, so the result is
/// never worse than either neighbourhood alone (each wins on different plan shapes in the benchmark).
/// Same input and seed give the same output unless the wall-clock budget hits. Trial evaluations run on the
/// working bitmap and are reverted (a swap is its own inverse).
/// </summary>
/// <param name="evaluator">Acceptance stack shared with the LLM path.</param>
/// <param name="pool">Untrimmed candidate pool (topPerIntent = int.MaxValue in production).</param>
/// <param name="fitness">Fitness evaluator used for the start score; must be the one inside <paramref name="evaluator"/>.</param>
/// <param name="options">Limits, pair cap, tabu tenure and seed.</param>
/// <param name="timeProvider">Clock for the wall-clock budget; null uses the system clock.</param>
/// <param name="sameDayValidator">Hard validator for the optional exhaustive same-day neighbourhood
/// (<see cref="DeterministicSearchOptions.IncludeAllSameDaySwaps"/>); required only when that option is on.</param>
public sealed class DeterministicHarmonyOptimizer
{
    private const double ImprovementEpsilon = 1e-9;
    private const int GuidByteCount = 16;
    private const int NeighbourhoodCount = 2;

    /// <summary>Intent label of swaps that only the exhaustive same-day neighbourhood produced.</summary>
    public const string SameDaySwapIntent = "same_day_swap";

    private readonly BatchEvaluator _evaluator;
    private readonly MoveCandidatePool _pool;
    private readonly IBitmapFitnessEvaluator _fitness;
    private readonly DeterministicSearchOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly PlanMutationValidator? _sameDayValidator;

    /// <summary>
    /// Production wiring: evaluator, pool, fitness and same-day validator all come from one
    /// <see cref="HolisticHarmonizerComponents"/>, so the start score and the pass comparison always use the
    /// fitness the acceptance stack uses.
    /// </summary>
    public DeterministicHarmonyOptimizer(
        HolisticHarmonizerComponents components,
        DeterministicSearchOptions options,
        TimeProvider? timeProvider = null)
        : this(
            (components ?? throw new ArgumentNullException(nameof(components))).Evaluator,
            components.Pool,
            components.Fitness,
            options,
            timeProvider,
            components.Validator)
    {
    }

    public DeterministicHarmonyOptimizer(
        BatchEvaluator evaluator,
        MoveCandidatePool pool,
        IBitmapFitnessEvaluator fitness,
        DeterministicSearchOptions options,
        TimeProvider? timeProvider = null,
        PlanMutationValidator? sameDayValidator = null)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(fitness);
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxIterations < 0 || options.MaxNoImprovementIterations < 1 || options.PairPoolCap < 0 || options.MaxEvaluations < 0
            || options.Restarts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Search limits must not be negative; the no-improvement limit and the restart count must be at least 1.");
        }

        if ((options.IncludeAllSameDaySwaps || options.AlternateNeighbourhoods) && sameDayValidator is null)
        {
            throw new ArgumentNullException(nameof(sameDayValidator), "The exhaustive same-day neighbourhood needs the hard validator.");
        }

        _sameDayValidator = sameDayValidator;
        _evaluator = evaluator;
        _pool = pool;
        _fitness = fitness;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public DeterministicSearchResult Run(
        HarmonyBitmap working,
        IProgress<HolisticHarmonizerProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(working);

        var startTimestamp = _timeProvider.GetTimestamp();
        var start = BitmapCloner.Clone(working);
        var fitnessBefore = _fitness.Evaluate(start).Fitness;
        HarmonyBitmap? bestBitmap = null;
        PassResult? best = null;
        long evaluations = 0;
        var maxCandidates = 0;
        var restartsRun = 0;
        var wallClockHit = false;

        Report(progress, 0, fitnessBefore, 0, startTimestamp);

        for (var restart = 0; restart < _options.Restarts; restart++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (restart > 0 && _timeProvider.GetElapsedTime(startTimestamp) >= _options.WallClockBudget)
            {
                wallClockHit = true;
                break;
            }

            var candidate = BitmapCloner.Clone(start);
            var pass = RunPass(candidate, restart, startTimestamp, cancellationToken);
            restartsRun++;
            evaluations += pass.Evaluations;
            maxCandidates = Math.Max(maxCandidates, pass.MaxCandidates);
            wallClockHit |= pass.StopReason == DeterministicSearchStopReason.WallClockBudget;
            if (best is null || pass.FitnessAfter > best.FitnessAfter + ImprovementEpsilon)
            {
                best = pass;
                bestBitmap = candidate;
            }

            Report(progress, restart + 1, best.FitnessAfter, best.Applied.Count, startTimestamp);
        }

        CopyCells(bestBitmap!, working);

        return new DeterministicSearchResult(
            best!.Applied,
            fitnessBefore,
            _fitness.Evaluate(working).Fitness,
            wallClockHit ? DeterministicSearchStopReason.WallClockBudget : best.StopReason,
            best.Iterations,
            evaluations,
            maxCandidates,
            (long)_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds)
        {
            RestartsRun = restartsRun,
            BestRestart = best.Restart,
        };
    }

    private PassResult RunPass(HarmonyBitmap working, int restart, long startTimestamp, CancellationToken cancellationToken)
    {
        var includeAllSameDay = UsesAllSameDaySwaps(restart);
        var run = new RunState(startTimestamp, _options.Seed, restart, FirstBestInOrder(restart));
        var tabu = new TabuList(_options.TabuTenure);
        var applied = new List<BatchEvaluation>();
        var current = _fitness.Evaluate(working).Fitness;
        var noImprovement = 0;
        var keptCount = 0;
        var iterationsRun = 0;
        var maxCandidates = 0;
        DeterministicSearchStopReason? stopReason = null;

        for (var iter = 0; iter < _options.MaxIterations; iter++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interruption(run) is { } before)
            {
                stopReason = before;
                break;
            }

            iterationsRun++;
            var candidates = CollectCandidates(working, includeAllSameDay);
            maxCandidates = Math.Max(maxCandidates, candidates.Count);

            var (best, interrupted) = SearchIteration(working, candidates, tabu.ActiveKeys(iter), run, cancellationToken);
            if (best is null)
            {
                stopReason = interrupted ?? DeterministicSearchStopReason.LocalOptimum;
                break;
            }

            var evaluation = _evaluator.Evaluate(working, best with { BatchId = run.NextBatchId(), LlmIteration = iter });
            run.Evaluations++;
            if (evaluation.Result != BatchAcceptance.Accepted)
            {
                stopReason = DeterministicSearchStopReason.LocalOptimum;
                break;
            }

            applied.Add(evaluation);
            tabu.Record(evaluation.AppliedSteps, iter);
            var improved = evaluation.ScoreAfter > current + ImprovementEpsilon;
            current = evaluation.ScoreAfter;
            noImprovement = improved ? 0 : noImprovement + 1;
            if (improved)
            {
                keptCount = applied.Count;
            }

            if (interrupted is not null)
            {
                stopReason = interrupted;
                break;
            }
            if (noImprovement >= _options.MaxNoImprovementIterations)
            {
                stopReason = DeterministicSearchStopReason.NoImprovementLimit;
                break;
            }
        }

        DropTrailingSidewaysMoves(working, applied, keptCount);

        return new PassResult(
            restart,
            applied,
            _fitness.Evaluate(working).Fitness,
            stopReason ?? DeterministicSearchStopReason.IterationLimit,
            iterationsRun,
            run.Evaluations,
            maxCandidates);
    }

    private static void CopyCells(HarmonyBitmap source, HarmonyBitmap target)
    {
        for (var r = 0; r < source.RowCount; r++)
        {
            for (var d = 0; d < source.DayCount; d++)
            {
                target.SetCell(r, d, source.GetCell(r, d));
            }
        }
    }

    /// <summary>
    /// Equal-score moves only pay off when a later move improves; those applied after the last strict
    /// improvement changed the plan for no measured gain and are rolled back (a swap is its own inverse).
    /// </summary>
    private static void DropTrailingSidewaysMoves(HarmonyBitmap working, List<BatchEvaluation> applied, int keptCount)
    {
        for (var b = applied.Count - 1; b >= keptCount; b--)
        {
            var steps = applied[b].AppliedSteps;
            for (var i = steps.Count - 1; i >= 0; i--)
            {
                PlanMutationValidator.Apply(working, steps[i]);
            }
        }
        applied.RemoveRange(keptCount, applied.Count - keptCount);
    }

    private (MutationBatch? Best, DeterministicSearchStopReason? Interrupted) SearchIteration(
        HarmonyBitmap working,
        IReadOnlyList<IntentSwap> candidates,
        IReadOnlySet<ForbiddenSwapKey> tabuKeys,
        RunState run,
        CancellationToken cancellationToken)
    {
        var best = new BestTracker(tabuKeys);
        var singles = new List<(IntentSwap Candidate, int Index, double Score)>(candidates.Count);

        for (var i = 0; i < candidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interruption(run) is { } stop)
            {
                return (best.Pick(run.TieBreaker), stop);
            }

            // Singles rejected on their own stay in the pair base: as the second step they can become valid
            // after the first one (an "enabling" pair), which is exactly what pairs add over singles.
            var batch = BuildBatch(candidates[i]);
            var evaluation = TrialEvaluate(working, batch, run);
            singles.Add((candidates[i], i, evaluation.ScoreAfter));
            best.Consider(evaluation, batch);
        }

        var pairBase = singles
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Index)
            .Take(_options.PairPoolCap)
            .Select(s => s.Candidate)
            .ToList();
        for (var i = 0; i < pairBase.Count; i++)
        {
            for (var j = i + 1; j < pairBase.Count; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Interruption(run) is { } stop)
                {
                    return (best.Pick(run.TieBreaker), stop);
                }

                var batch = BuildBatch(pairBase[i], pairBase[j]);
                best.Consider(TrialEvaluate(working, batch, run), batch);
            }
        }

        return (best.Pick(run.TieBreaker), null);
    }

    private BatchEvaluation TrialEvaluate(HarmonyBitmap working, MutationBatch batch, RunState run)
    {
        var evaluation = _evaluator.Evaluate(working, batch);
        run.Evaluations++;
        for (var i = evaluation.AppliedSteps.Count - 1; i >= 0; i--)
        {
            PlanMutationValidator.Apply(working, evaluation.AppliedSteps[i]);
        }
        return evaluation;
    }

    private DeterministicSearchStopReason? Interruption(RunState run)
    {
        if (run.Evaluations >= _options.MaxEvaluations)
        {
            return DeterministicSearchStopReason.EvaluationLimit;
        }
        if (_timeProvider.GetElapsedTime(run.StartTimestamp) >= _options.WallClockBudget)
        {
            return DeterministicSearchStopReason.WallClockBudget;
        }
        return null;
    }

    private bool UsesAllSameDaySwaps(int restart)
        => _options.IncludeAllSameDaySwaps || (_options.AlternateNeighbourhoods && restart % NeighbourhoodCount == 1);

    /// <summary>The first pass of each neighbourhood breaks ties by candidate order; later passes by seeded random.</summary>
    private bool FirstBestInOrder(int restart)
        => restart < (_options.AlternateNeighbourhoods && !_options.IncludeAllSameDaySwaps ? NeighbourhoodCount : 1);

    private List<IntentSwap> CollectCandidates(HarmonyBitmap working, bool includeAllSameDay)
    {
        var seen = new HashSet<(int, int, int, int)>();
        var result = new List<IntentSwap>();
        foreach (var intent in HolisticIntent.All)
        {
            foreach (var candidate in _pool.Generate(working, intent))
            {
                var key = (Math.Min(candidate.RowA, candidate.RowB), Math.Max(candidate.RowA, candidate.RowB),
                    Math.Min(candidate.DayA, candidate.DayB), Math.Max(candidate.DayA, candidate.DayB));
                if (seen.Add(key))
                {
                    result.Add(new IntentSwap(intent, new PlanCellSwap(candidate.RowA, candidate.DayA, candidate.RowB, candidate.DayB, string.Empty)));
                }
            }
        }
        if (includeAllSameDay)
        {
            AddAllSameDaySwaps(working, seen, result);
        }
        return result;
    }

    /// <summary>
    /// Every hard-valid same-day swap of two different cells, appended after the pool candidates (row-major,
    /// day by day) so the pool's ranking still decides ties within the pair base.
    /// </summary>
    private void AddAllSameDaySwaps(
        HarmonyBitmap working,
        HashSet<(int, int, int, int)> seen,
        List<IntentSwap> result)
    {
        for (var day = 0; day < working.DayCount; day++)
        {
            for (var rowA = 0; rowA < working.RowCount; rowA++)
            {
                for (var rowB = rowA + 1; rowB < working.RowCount; rowB++)
                {
                    var swap = new PlanCellSwap(rowA, day, rowB, day, string.Empty);
                    if (!seen.Add((rowA, rowB, day, day))
                        || _sameDayValidator!.Validate(working, swap) is not null)
                    {
                        continue;
                    }
                    result.Add(new IntentSwap(SameDaySwapIntent, swap));
                }
            }
        }
    }

    private static MutationBatch BuildBatch(params IntentSwap[] steps)
        => new(Guid.Empty, steps[0].Intent, LlmIteration: 0, Steps: steps.Select(s => s.Swap).ToArray());

    /// <summary>One tick per finished pass: the progress bar counts passes (restarts), not inner iterations.</summary>
    private void Report(IProgress<HolisticHarmonizerProgress>? progress, int passesDone, double bestFitness, int applied, long startTimestamp)
        => progress?.Report(new HolisticHarmonizerProgress(
            IterationIndex: passesDone,
            MaxIterations: _options.Restarts,
            BestFitness: bestFitness,
            AcceptedBatchCount: applied,
            RejectedBatchCount: 0,
            ElapsedMs: (long)_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds));

    private sealed record PassResult(
        int Restart,
        List<BatchEvaluation> Applied,
        double FitnessAfter,
        DeterministicSearchStopReason StopReason,
        int Iterations,
        long Evaluations,
        int MaxCandidates);

    private sealed record IntentSwap(string Intent, PlanCellSwap Swap);

    private sealed class RunState
    {
        private readonly Random _ids;

        public RunState(long startTimestamp, int seed, int restart, bool firstBestInOrder)
        {
            StartTimestamp = startTimestamp;
            TieBreaker = firstBestInOrder ? null : new Random(seed + restart);
            _ids = new Random(seed + restart);
        }

        public long StartTimestamp { get; }

        /// <summary>Null takes the first best move in candidate order.</summary>
        public Random? TieBreaker { get; }

        public long Evaluations { get; set; }

        public Guid NextBatchId()
        {
            var bytes = new byte[GuidByteCount];
            _ids.NextBytes(bytes);
            return new Guid(bytes);
        }
    }

    private sealed class BestTracker
    {
        private readonly List<MutationBatch> _ties = new();
        private readonly IReadOnlySet<ForbiddenSwapKey> _tabuKeys;
        private double _bestScore = double.NegativeInfinity;

        public BestTracker(IReadOnlySet<ForbiddenSwapKey> tabuKeys)
        {
            _tabuKeys = tabuKeys;
        }

        public void Consider(BatchEvaluation evaluation, MutationBatch batch)
        {
            if (evaluation.Result != BatchAcceptance.Accepted)
            {
                return;
            }
            var strictlyImproving = evaluation.ScoreAfter > evaluation.ScoreBefore + ImprovementEpsilon;
            if (!strictlyImproving && IsTabu(batch))
            {
                return;
            }
            if (evaluation.ScoreAfter > _bestScore + ImprovementEpsilon)
            {
                _bestScore = evaluation.ScoreAfter;
                _ties.Clear();
                _ties.Add(batch);
            }
            else if (Math.Abs(evaluation.ScoreAfter - _bestScore) <= ImprovementEpsilon)
            {
                _ties.Add(batch);
            }
        }

        private bool IsTabu(MutationBatch batch)
            => _tabuKeys.Count > 0
               && batch.Steps.Any(s => s.DayA == s.DayB && _tabuKeys.Contains(ForbiddenSwapKey.From(s)));

        public MutationBatch? Pick(Random? tieBreaker)
            => _ties.Count switch
            {
                0 => null,
                1 => _ties[0],
                _ => tieBreaker is null ? _ties[0] : _ties[tieBreaker.Next(_ties.Count)],
            };
    }
}
