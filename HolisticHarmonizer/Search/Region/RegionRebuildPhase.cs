// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Evolution;
using Klacks.ScheduleOptimizer.Harmonizer.Global;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Search.Region;

/// <summary>
/// Second phase of the deterministic stage 3: after the local search has reached its best per-row score, regions of the
/// plan are rebuilt to raise the harmony BETWEEN the rows without giving anything up the first instance measures.
/// <para>
/// Iterated local search over rounds: each round proposes <see cref="RegionRebuildOptions.RegionsPerRound"/> regions
/// on the current plan and rebuilds each one independently. The synthesis then combines the admitted rebuilds of
/// non-touching regions into one plan (Construct-Merge-Solve style: collect the building blocks, keep the best
/// admissible combination), re-checking every combination with both instances, and falls back to the best single
/// rebuild when the combination is not better. The next round starts from the result. The phase stops after
/// <see cref="RegionRebuildOptions.MaxStaleRounds"/> rounds without a harmony gain.
/// </para>
/// <para>
/// The first-instance floor is the per-row fitness at the start of each round: a round is applied only when it keeps or
/// raises the fitness the plan has at that moment, so the fitness chain of the applied batches never falls and the
/// harmony only ever rises. Each applied round is reported as one batch (the regions of a round never touch, so their
/// swaps are independent).
/// </para>
/// </summary>
/// <param name="admission">First instance.</param>
/// <param name="harmony">Second instance.</param>
/// <param name="options">Rounds, regions, beam and budget.</param>
/// <param name="timeProvider">Clock for the safety-net budget; null uses the system clock.</param>
public sealed class RegionRebuildPhase
{
    private const double Epsilon = 1e-9;
    private const int GuidByteCount = 16;

    private readonly RegionAdmission _admission;
    private readonly GlobalHarmonyEvaluator _harmony;
    private readonly RegionRebuildOptions _options;
    private readonly RegionRebuilder _rebuilder;
    private readonly TimeProvider _timeProvider;

    public RegionRebuildPhase(
        RegionAdmission admission,
        GlobalHarmonyEvaluator harmony,
        RegionRebuildOptions options,
        TimeProvider? timeProvider = null)
    {
        _admission = admission ?? throw new ArgumentNullException(nameof(admission));
        _harmony = harmony ?? throw new ArgumentNullException(nameof(harmony));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _rebuilder = new RegionRebuilder(admission, harmony, options.BeamWidth);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Rebuilds regions of <paramref name="working"/> in place.</summary>
    public RegionRebuildResult Run(HarmonyBitmap working, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(working);

        var startTimestamp = _timeProvider.GetTimestamp();
        var budget = TimeSpan.FromSeconds(Math.Max(0, _options.WallClockBudgetSeconds));
        var rng = new Random(_options.Seed);
        var rhythm = new RhythmMatrix(working);
        var harmonyBefore = _harmony.Evaluate(working, rhythm);
        var current = harmonyBefore;
        var applied = new List<BatchEvaluation>();
        var stale = 0;
        var rounds = 0;
        var candidatesFound = 0;
        var budgetHit = false;

        for (var round = 0; round < _options.Rounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_timeProvider.GetElapsedTime(startTimestamp) >= budget)
            {
                budgetHit = true;
                break;
            }

            rounds++;
            var floor = _admission.Fitness(working);
            var candidates = new List<RegionCandidate>();
            foreach (var region in RegionProposer.Propose(working, rhythm, _options, rng))
            {
                if (_timeProvider.GetElapsedTime(startTimestamp) >= budget)
                {
                    budgetHit = true;
                    break;
                }

                var candidate = _rebuilder.Rebuild(working, rhythm, floor, current, region, cancellationToken);
                if (candidate is not null)
                {
                    candidates.Add(candidate);
                }
            }

            candidatesFound += candidates.Count;
            var outcome = Synthesize(working, rhythm, floor, current, candidates);
            if (outcome is null)
            {
                stale++;
                if (stale >= _options.MaxStaleRounds || budgetHit)
                {
                    break;
                }

                continue;
            }

            CopyCells(outcome.Bitmap, working);
            rhythm = outcome.Rhythm;
            current = outcome.Harmony;
            stale = 0;
            applied.Add(new BatchEvaluation(
                NextBatchId(rng),
                RegionRebuilder.Intent,
                BatchAcceptance.Accepted,
                outcome.StepsPerRegion.SelectMany(steps => steps).ToList(),
                [],
                null,
                floor,
                _admission.Fitness(working)));

            if (budgetHit)
            {
                break;
            }
        }

        return new RegionRebuildResult(applied, harmonyBefore, current, rounds, candidatesFound, budgetHit);
    }

    private SynthesisOutcome? Synthesize(
        HarmonyBitmap working,
        RhythmMatrix rhythm,
        double floor,
        GlobalHarmonyScore current,
        List<RegionCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var ordered = candidates.OrderByDescending(c => c.Harmony.Total).ToList();
        var merged = BitmapCloner.Clone(working);
        var mergedRhythm = rhythm.Clone();
        var mergedHarmony = current;
        var mergedFitness = _admission.Fitness(working);
        var kept = new List<RegionCandidate>();
        foreach (var candidate in ordered)
        {
            if (kept.Any(k => k.Region.Touches(candidate.Region)))
            {
                continue;
            }

            var trial = BitmapCloner.Clone(merged);
            CopyRegion(candidate.Bitmap, trial, candidate.Region);
            var allSteps = kept.SelectMany(k => k.Steps).Concat(candidate.Steps).ToList();
            var allRows = kept.SelectMany(k => k.Region.Rows).Concat(candidate.Region.Rows).Distinct().ToList();
            var fitness = _admission.Admit(working, trial, allRows, allSteps, floor);
            if (fitness is null)
            {
                continue;
            }

            var trialRhythm = mergedRhythm.Clone();
            trialRhythm.Refresh(trial, candidate.Region.Rows);
            var harmony = _harmony.Evaluate(trial, trialRhythm);
            if (harmony.Total <= mergedHarmony.Total + Epsilon)
            {
                continue;
            }

            merged = trial;
            mergedRhythm = trialRhythm;
            mergedHarmony = harmony;
            mergedFitness = fitness.Value;
            kept.Add(candidate);
        }

        var bestSingle = ordered[0];
        if (kept.Count == 0 || bestSingle.Harmony.Total > mergedHarmony.Total + Epsilon)
        {
            return new SynthesisOutcome(bestSingle.Bitmap, bestSingle.Rhythm, bestSingle.Harmony, bestSingle.Fitness, [bestSingle.Steps]);
        }

        return new SynthesisOutcome(merged, mergedRhythm, mergedHarmony, mergedFitness, kept.Select(k => k.Steps).ToList());
    }

    private static void CopyRegion(HarmonyBitmap source, HarmonyBitmap target, HarmonyRegion region)
    {
        foreach (var r in region.Rows)
        {
            for (var d = region.DayFrom; d <= region.DayTo; d++)
            {
                target.SetCell(r, d, source.GetCell(r, d));
            }
        }
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

    private static Guid NextBatchId(Random rng)
    {
        var bytes = new byte[GuidByteCount];
        rng.NextBytes(bytes);
        return new Guid(bytes);
    }

    private sealed record SynthesisOutcome(
        HarmonyBitmap Bitmap,
        RhythmMatrix Rhythm,
        GlobalHarmonyScore Harmony,
        double Fitness,
        IReadOnlyList<IReadOnlyList<PlanCellSwap>> StepsPerRegion);
}
