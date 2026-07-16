// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using System.Text.Json;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Harmonizer.Evolution;
using Klacks.ScheduleOptimizer.Harmonizer.Scorer;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

namespace Klacks.ScheduleOptimizer.Scoring;

/// <summary>
/// Serialises a wizard run into the versioned, engine-tagged JSON captured for the (deferred)
/// preference-learner. Mirrors the schema and camelCase JSON options of <c>ScenarioScoreSerializer</c>.
/// Emits three engine tags: <c>tokenEvolution</c> (Wizard 1) with the full stage-component breakdown,
/// and <c>harmonizer</c> (Wizard 2) / <c>holistic</c> (Wizard 3) with the real HarmonyScorer component
/// breakdown (the seven <see cref="RowFeatures"/>), each together with the effective config snapshot and
/// the instance context features.
/// </summary>
public static class EngineScoreSerializer
{
    public const int SchemaVersion = 1;
    public const string TokenEvolutionEngineTag = "tokenEvolution";
    public const string HarmonizerEngineTag = "harmonizer";
    public const string HolisticEngineTag = "holistic";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Builds the <c>tokenEvolution</c> snapshot JSON. A score is only interpretable together with the
    /// weights it was produced under and the instance features it was measured on, so both the effective
    /// config and the run context are embedded.
    /// </summary>
    /// <param name="result">Stage aggregates and Stage-3/4 component breakdown of the winning individual</param>
    /// <param name="config">Effective tuning config the run used (fitness-shaping weights, seed, population mix)</param>
    /// <param name="context">Run input; supplies the instance features and the warm-start flag</param>
    public static string SerializeTokenEvolution(
        DetailedFitnessResult result,
        TokenEvolutionConfig config,
        CoreWizardContext context)
    {
        var snapshot = new
        {
            v = SchemaVersion,
            engine = TokenEvolutionEngineTag,
            config = new
            {
                stage1RankDecay = config.FitnessStage1RankDecay,
                stage2Decay = config.FitnessStage2Decay,
                stage3Weights = new
                {
                    blockOrder = config.FitnessStage3BlockOrder,
                    blacklist = config.FitnessStage3Blacklist,
                    location = config.FitnessStage3Location,
                    maxGap = config.FitnessStage3MaxGap,
                },
                initAuctionRatio = config.InitAuctionRatio,
                initWarmStartRatio = config.InitWarmStartRatio,
                seed = config.RandomSeed,
            },
            stages = new
            {
                s0 = result.Stage0,
                s1 = result.Stage1,
                s2 = result.Stage2,
                s3 = result.Stage3,
                s4 = result.Stage4,
            },
            stage3 = new
            {
                blockOrder = result.Stage3Components.BlockOrder,
                blacklist = result.Stage3Components.Blacklist,
                location = result.Stage3Components.Location,
                maxGap = result.Stage3Components.MaxGap,
            },
            stage4 = new
            {
                fairness = result.Stage4Components.Fairness,
                minimumHours = result.Stage4Components.MinimumHours,
                blockSymmetry = result.Stage4Components.BlockSymmetry,
            },
            context = new
            {
                agents = context.Agents.Count,
                shifts = context.Shifts.Count,
                days = PeriodDayCount(context),
                lockedRatio = ComputeLockedRatio(context),
                warmStart = context.WarmStartAssignments.Count > 0,
            },
        };

        return JsonSerializer.Serialize(snapshot, Options);
    }

    /// <summary>
    /// Builds the <c>harmonizer</c> (Wizard 2) snapshot JSON. Unlike the five-stage token-evolution world,
    /// the harmonizer has no fitness stages; its real breakdown is the seven per-row HarmonyScorer features
    /// (block-size uniformity, rest uniformity, block homogeneity, transition compliance, shift-type rotation,
    /// preferred-shift fraction, target-hours deviation) averaged across the rows. <c>fitness</c> is the
    /// row-weighted global score the evaluator produced; the components are unweighted per-row means, so both
    /// are captured but they measure different things (the objective vs. an instance-feature summary).
    /// </summary>
    /// <param name="bitmap">The harmonised bitmap whose per-row features are aggregated</param>
    /// <param name="config">Effective evolution config the harmonizer run used</param>
    /// <param name="globalFitness">Row-weighted global harmony fitness of the bitmap, in [0,1]</param>
    public static string SerializeHarmonizer(HarmonyBitmap bitmap, HarmonizerEvolutionConfig config, double globalFitness)
    {
        var snapshot = new
        {
            v = SchemaVersion,
            engine = HarmonizerEngineTag,
            fitness = globalFitness,
            components = BuildHarmonyComponents(bitmap),
            config = new
            {
                populationSize = config.PopulationSize,
                maxGenerations = config.MaxGenerations,
                eliteCount = config.EliteCount,
                tournamentSize = config.TournamentSize,
                stochasticMutationsPerOffspring = config.StochasticMutationsPerOffspring,
                stagnationGenerations = config.StagnationGenerations,
                seed = config.Seed,
            },
            context = BuildHarmonyContext(bitmap),
        };

        return JsonSerializer.Serialize(snapshot, Options);
    }

    /// <summary>
    /// Builds the <c>holistic</c> (Wizard 3) snapshot JSON. Uses the same real HarmonyScorer component
    /// breakdown as the harmonizer tag because Wizard 3 optimises the same bitmap representation with the
    /// same scorer. Wizard 3 runs an LLM inner-loop rather than an evolution config, so no <c>config</c>
    /// block is emitted (its run parameters are not a HarmonizerEvolutionConfig — a deliberate signal gap
    /// rather than fabricated values).
    /// </summary>
    /// <param name="bitmap">The harmonised bitmap whose per-row features are aggregated</param>
    /// <param name="globalFitness">Global harmony fitness of the bitmap, in [0,1]</param>
    public static string SerializeHolistic(HarmonyBitmap bitmap, double globalFitness)
    {
        var snapshot = new
        {
            v = SchemaVersion,
            engine = HolisticEngineTag,
            fitness = globalFitness,
            components = BuildHarmonyComponents(bitmap),
            context = BuildHarmonyContext(bitmap),
        };

        return JsonSerializer.Serialize(snapshot, Options);
    }

    /// <summary>
    /// Averages the seven real <see cref="RowFeatures"/> across every row of the bitmap. Extracts the
    /// features directly (no Mamdani inference) because only the crisp inputs are needed, not the score.
    /// </summary>
    private static object BuildHarmonyComponents(HarmonyBitmap bitmap)
    {
        var rows = bitmap.RowCount;
        if (rows == 0)
        {
            return new
            {
                blockSizeUniformity = 0.0,
                restUniformity = 0.0,
                blockHomogeneity = 0.0,
                transitionCompliance = 0.0,
                shiftTypeRotation = 0.0,
                preferredShiftFraction = 0.0,
                targetHoursDeviation = 0.0,
            };
        }

        double blockSizeUniformity = 0, restUniformity = 0, blockHomogeneity = 0, transitionCompliance = 0;
        double shiftTypeRotation = 0, preferredShiftFraction = 0, targetHoursDeviation = 0;
        for (var r = 0; r < rows; r++)
        {
            var f = RowFeatureExtractor.Extract(bitmap, r);
            blockSizeUniformity += f.BlockSizeUniformity;
            restUniformity += f.RestUniformity;
            blockHomogeneity += f.BlockHomogeneity;
            transitionCompliance += f.TransitionCompliance;
            shiftTypeRotation += f.ShiftTypeRotation;
            preferredShiftFraction += f.PreferredShiftFraction;
            targetHoursDeviation += f.TargetHoursDeviation;
        }

        return new
        {
            blockSizeUniformity = blockSizeUniformity / rows,
            restUniformity = restUniformity / rows,
            blockHomogeneity = blockHomogeneity / rows,
            transitionCompliance = transitionCompliance / rows,
            shiftTypeRotation = shiftTypeRotation / rows,
            preferredShiftFraction = preferredShiftFraction / rows,
            targetHoursDeviation = targetHoursDeviation / rows,
        };
    }

    private static object BuildHarmonyContext(HarmonyBitmap bitmap)
    {
        return new
        {
            agents = bitmap.RowCount,
            days = bitmap.DayCount,
            lockedRatio = ComputeHarmonyLockedRatio(bitmap),
        };
    }

    /// <summary>
    /// Locked-cell density = locked cells / (rows x days), a normalisation feature telling the learner how
    /// constrained the instance was. Break cells are locked in the harmonizer model, so they count here.
    /// </summary>
    private static double ComputeHarmonyLockedRatio(HarmonyBitmap bitmap)
    {
        var cells = bitmap.RowCount * bitmap.DayCount;
        if (cells == 0)
        {
            return 0;
        }

        var locked = 0;
        for (var r = 0; r < bitmap.RowCount; r++)
        {
            for (var d = 0; d < bitmap.DayCount; d++)
            {
                if (bitmap.GetCell(r, d).IsLocked)
                {
                    locked++;
                }
            }
        }

        return locked / (double)cells;
    }

    private static int PeriodDayCount(CoreWizardContext context)
    {
        return context.PeriodUntil.DayNumber - context.PeriodFrom.DayNumber + 1;
    }

    /// <summary>
    /// Locked-cell density = LockedWorks / (agents x days), the fraction of the grid the GA could not move.
    /// Breaks are NOT counted here (they are a separate immutability source); this is a pure locked-work ratio,
    /// a normalisation feature so the learner can compare a score against how constrained the instance was.
    /// </summary>
    private static double ComputeLockedRatio(CoreWizardContext context)
    {
        var cells = context.Agents.Count * PeriodDayCount(context);
        return cells > 0 ? context.LockedWorks.Count / (double)cells : 0;
    }
}
