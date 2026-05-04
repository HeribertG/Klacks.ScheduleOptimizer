// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Common.Fuzzy;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Harmonizer.Scorer;

/// <summary>
/// Computes a harmony score in [0,1] for a single bitmap row using the Mamdani fuzzy engine.
/// The four crisp inputs are produced by RowFeatureExtractor; the rule base is loaded from
/// the embedded resource `harmony-rules.json` and can be overridden via the constructor.
/// </summary>
public sealed class HarmonyScorer
{
    private readonly MamdaniInferenceEngine _engine;

    public HarmonyScorer()
        : this(HarmonyRuleBaseLoader.LoadDefault())
    {
    }

    public HarmonyScorer(IReadOnlyList<FuzzyRule> rules)
    {
        _engine = new MamdaniInferenceEngine(
            HarmonyLinguisticVariables.BuildInputs(),
            HarmonyLinguisticVariables.BuildOutput(),
            rules);
    }

    public RowScore Score(HarmonyBitmap bitmap, int rowIndex)
    {
        var features = RowFeatureExtractor.Extract(bitmap, rowIndex);
        var inputs = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [HarmonyLinguisticVariables.BlockSizeUniformity] = features.BlockSizeUniformity,
            [HarmonyLinguisticVariables.RestUniformity] = features.RestUniformity,
            [HarmonyLinguisticVariables.BlockHomogeneity] = features.BlockHomogeneity,
            [HarmonyLinguisticVariables.TransitionCompliance] = features.TransitionCompliance,
            [HarmonyLinguisticVariables.ShiftTypeRotation] = features.ShiftTypeRotation,
            [HarmonyLinguisticVariables.PreferredShiftFraction] = features.PreferredShiftFraction,
        };

        var inference = _engine.Infer(inputs);
        return new RowScore(inference.CrispOutput, features, inference.FiredRules);
    }
}

/// <param name="Score">Defuzzified harmony score in [0,1]; higher is better</param>
/// <param name="Features">The crisp inputs that produced the score</param>
/// <param name="FiredRules">Rules that contributed to the score (for diagnostics)</param>
public sealed record RowScore(double Score, RowFeatures Features, IReadOnlyList<RuleActivation> FiredRules);
