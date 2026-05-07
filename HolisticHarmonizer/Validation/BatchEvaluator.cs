// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Evolution;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Validation;

/// <summary>
/// Evaluates a <see cref="MutationBatch"/> as one atomic transformation: applies steps in order,
/// stops at the first hard-constraint violation (longest valid prefix), then enforces
/// Score-Greedy on the prefix end-state. If the prefix score does not regress, the prefix is
/// kept; otherwise the prefix is reverted and the whole batch is reported as
/// <see cref="BatchAcceptance.WouldDegrade"/>.
/// </summary>
/// <param name="mutationValidator">Hard-constraint layer used per step (locks, bounds, caps,
/// pause, availability).</param>
/// <param name="fitnessEvaluator">Computes the global harmony score before/after the batch.</param>
public sealed class BatchEvaluator
{
    private readonly PlanMutationValidator _mutationValidator;
    private readonly HarmonyFitnessEvaluator _fitnessEvaluator;

    public BatchEvaluator(PlanMutationValidator mutationValidator, HarmonyFitnessEvaluator fitnessEvaluator)
    {
        _mutationValidator = mutationValidator;
        _fitnessEvaluator = fitnessEvaluator;
    }

    public BatchEvaluation Evaluate(HarmonyBitmap workingBitmap, MutationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(workingBitmap);
        ArgumentNullException.ThrowIfNull(batch);

        var scoreBefore = _fitnessEvaluator.Evaluate(workingBitmap).Fitness;

        var appliedSteps = new List<PlanCellSwap>(batch.Steps.Count);
        var rejections = new List<PlanMutationRejection>();
        int? stoppedAtStep = null;

        for (var i = 0; i < batch.Steps.Count; i++)
        {
            var step = batch.Steps[i];
            var rejection = _mutationValidator.Validate(workingBitmap, step);
            if (rejection is null)
            {
                PlanMutationValidator.Apply(workingBitmap, step);
                appliedSteps.Add(step);
            }
            else
            {
                rejections.Add(rejection);
                stoppedAtStep = i;
                break;
            }
        }

        if (appliedSteps.Count == 0)
        {
            return new BatchEvaluation(
                batch.BatchId,
                batch.Intent,
                BatchAcceptance.Rejected,
                AppliedSteps: [],
                Rejections: rejections,
                StoppedAtStep: stoppedAtStep,
                ScoreBefore: scoreBefore,
                ScoreAfter: scoreBefore);
        }

        var scoreAfter = _fitnessEvaluator.Evaluate(workingBitmap).Fitness;

        if (scoreAfter >= scoreBefore)
        {
            var fullyApplied = stoppedAtStep is null;
            return new BatchEvaluation(
                batch.BatchId,
                batch.Intent,
                fullyApplied ? BatchAcceptance.Accepted : BatchAcceptance.PartiallyAccepted,
                AppliedSteps: appliedSteps,
                Rejections: rejections,
                StoppedAtStep: stoppedAtStep,
                ScoreBefore: scoreBefore,
                ScoreAfter: scoreAfter);
        }

        RevertPrefix(workingBitmap, appliedSteps);

        return new BatchEvaluation(
            batch.BatchId,
            batch.Intent,
            BatchAcceptance.WouldDegrade,
            AppliedSteps: [],
            Rejections: rejections,
            StoppedAtStep: stoppedAtStep,
            ScoreBefore: scoreBefore,
            ScoreAfter: scoreBefore);
    }

    private static void RevertPrefix(HarmonyBitmap bitmap, IReadOnlyList<PlanCellSwap> appliedSteps)
    {
        for (var i = appliedSteps.Count - 1; i >= 0; i--)
        {
            PlanMutationValidator.Apply(bitmap, appliedSteps[i]);
        }
    }
}
