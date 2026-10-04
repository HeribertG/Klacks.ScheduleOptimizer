// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Conductor;
using Klacks.ScheduleOptimizer.Harmonizer.Evolution;
using Klacks.ScheduleOptimizer.Harmonizer.Rules;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Candidates;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Committee;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Committee.Agents;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Validation;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Search;

/// <summary>
/// Composition root of the stage-3 acceptance stack built from one <see cref="BitmapInput"/>: hard validator,
/// five-agent committee, target-hours guard, score-greedy batch evaluator and the candidate pool over the
/// three intent generators.
/// The deterministic engine, the LLM engine and the harmonizer eval runner all build through here, so every
/// mode judges moves identically. Callers must score with <see cref="Fitness"/> (the evaluator the acceptance
/// stack uses), never with the evaluator they passed in, so a later wrapper around it reaches every caller.
/// Planning rules (BitmapInput.Rules) hook in here only: hard rules into the validator (same-day and cross-day),
/// soft rules as <see cref="RuleAwareBitmapFitnessEvaluator"/> AROUND the passed evaluator (outside its row memo),
/// MaxConsecutiveOfKind runs make the rotation agent abstain. Without rules nothing is wrapped or added.
/// </summary>
/// <param name="Validator">Hard-constraint validator shared by pool and evaluator.</param>
/// <param name="Evaluator">Hard validator + committee + score-greedy.</param>
/// <param name="Pool">Candidate pool (same-day swaps of all three intents).</param>
/// <param name="Fitness">Fitness evaluator used inside <paramref name="Evaluator"/>.</param>
/// <param name="Rules">Planning-rule state of the run; null when the input carries no planning rule.</param>
public sealed record HolisticHarmonizerComponents(
    PlanMutationValidator Validator,
    BatchEvaluator Evaluator,
    MoveCandidatePool Pool,
    IBitmapFitnessEvaluator Fitness,
    BitmapRuleRuntime? Rules = null)
{
    /// <summary>Pool cap that keeps every validated candidate (the deterministic search ranks them itself).</summary>
    public const int UntrimmedPool = int.MaxValue;

    /// <param name="input">Schedule context (availability, boundary assignments, eligibility, time windows).</param>
    /// <param name="fitness">Base fitness evaluator (memoized in the deterministic mode, plain in the LLM paths).</param>
    /// <param name="topPerIntent">Per-intent cap of the candidate pool; int.MaxValue keeps it untrimmed.</param>
    /// <param name="softPenaltyWeight">lambda of the soft planning-rule term (only used when the input has a soft rule).</param>
    public static HolisticHarmonizerComponents Build(
        BitmapInput input,
        IBitmapFitnessEvaluator fitness,
        int topPerIntent,
        double softPenaltyWeight = RuleAwareBitmapFitnessEvaluator.DefaultSoftPenaltyWeight)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(fitness);

        var rules = BitmapRuleRuntime.TryCreate(input);
        var validator = new PlanMutationValidator(DomainAwareReplaceValidator.ForInput(input, rules), input.RestrictedTimeWindows);
        var committee = new ConstraintAgentCommittee(new IConstraintAgent[]
        {
            new HoursConstraintAgent(),
            new PauseConstraintAgent(input.BoundaryAssignments),
            new ConsecutiveConstraintAgent(input.BoundaryAssignments),
            new RotationConstraintAgent(rules is null ? null : new PlanningRuleRunGovernance(rules.Rules)),
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
        var effectiveFitness = rules is { HasSoftRules: true }
            ? new RuleAwareBitmapFitnessEvaluator(fitness, rules, softPenaltyWeight)
            : fitness;
        var evaluator = new BatchEvaluator(validator, effectiveFitness, committee, new TargetHoursDeviationGuard());
        return new HolisticHarmonizerComponents(validator, evaluator, pool, effectiveFitness, rules);
    }
}
