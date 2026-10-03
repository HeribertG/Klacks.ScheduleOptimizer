// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Conductor;
using Klacks.ScheduleOptimizer.Harmonizer.Evolution;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Candidates;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Committee;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Committee.Agents;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Validation;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Search;

/// <summary>
/// Composition root of the stage-3 acceptance stack built from one <see cref="BitmapInput"/>: hard validator,
/// five-agent committee, score-greedy batch evaluator and the candidate pool over the three intent generators.
/// The deterministic engine, the LLM engine and the harmonizer eval runner all build through here, so every
/// mode judges moves identically. Callers must score with <see cref="Fitness"/> (the evaluator the acceptance
/// stack uses), never with the evaluator they passed in, so a later wrapper around it reaches every caller.
/// </summary>
/// <param name="Validator">Hard-constraint validator shared by pool and evaluator.</param>
/// <param name="Evaluator">Hard validator + committee + score-greedy.</param>
/// <param name="Pool">Candidate pool (same-day swaps of all three intents).</param>
/// <param name="Fitness">Fitness evaluator used inside <paramref name="Evaluator"/>.</param>
public sealed record HolisticHarmonizerComponents(
    PlanMutationValidator Validator,
    BatchEvaluator Evaluator,
    MoveCandidatePool Pool,
    IBitmapFitnessEvaluator Fitness)
{
    /// <summary>Pool cap that keeps every validated candidate (the deterministic search ranks them itself).</summary>
    public const int UntrimmedPool = int.MaxValue;

    /// <param name="input">Schedule context (availability, boundary assignments, eligibility, time windows).</param>
    /// <param name="fitness">Base fitness evaluator (memoized in the deterministic mode, plain in the LLM paths).</param>
    /// <param name="topPerIntent">Per-intent cap of the candidate pool; int.MaxValue keeps it untrimmed.</param>
    public static HolisticHarmonizerComponents Build(BitmapInput input, IBitmapFitnessEvaluator fitness, int topPerIntent)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(fitness);

        var validator = new PlanMutationValidator(
            new DomainAwareReplaceValidator(input.Availability, input.BoundaryAssignments, input.IneligibleAssignments),
            input.RestrictedTimeWindows);
        var committee = new ConstraintAgentCommittee(new IConstraintAgent[]
        {
            new HoursConstraintAgent(),
            new PauseConstraintAgent(input.BoundaryAssignments),
            new ConsecutiveConstraintAgent(input.BoundaryAssignments),
            new RotationConstraintAgent(),
            new PreferenceConstraintAgent(),
        });
        var pool = new MoveCandidatePool(
            validator,
            new IMoveCandidateGenerator[]
            {
                new ConsolidateBlockCandidateGenerator(),
                new EnlargePauseCandidateGenerator(),
                new RedistributeLoadCandidateGenerator(),
            },
            topPerIntent);
        return new HolisticHarmonizerComponents(validator, new BatchEvaluator(validator, fitness, committee), pool, fitness);
    }
}
