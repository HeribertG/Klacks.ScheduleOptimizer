// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Common.Fuzzy;

namespace Klacks.ScheduleOptimizer.Harmonizer.Scorer;

/// <summary>
/// Linguistic variables used by the harmony scorer. All four input variables share the same
/// [0,1] domain partition (Low / Medium / High); the output HarmonyScore uses a four-term scale
/// so the conductor can distinguish "Mediocre" rows from outright "Poor" ones.
/// </summary>
public static class HarmonyLinguisticVariables
{
    public const string BlockSizeUniformity = "BlockSizeUniformity";
    public const string RestUniformity = "RestUniformity";
    public const string BlockHomogeneity = "BlockHomogeneity";
    public const string TransitionCompliance = "TransitionCompliance";
    public const string ShiftTypeRotation = "ShiftTypeRotation";
    public const string PreferredShiftFraction = "PreferredShiftFraction";
    public const string HarmonyScore = "HarmonyScore";

    public const string TermLow = "Low";
    public const string TermMedium = "Medium";
    public const string TermHigh = "High";

    public const string OutputPoor = "Poor";
    public const string OutputMediocre = "Mediocre";
    public const string OutputGood = "Good";
    public const string OutputExcellent = "Excellent";

    public static IReadOnlyDictionary<string, LinguisticVariable> BuildInputs()
    {
        var unitInterval = BuildUnitIntervalVariable();
        return new Dictionary<string, LinguisticVariable>(StringComparer.Ordinal)
        {
            [BlockSizeUniformity] = WithName(unitInterval, BlockSizeUniformity),
            [RestUniformity] = WithName(unitInterval, RestUniformity),
            [BlockHomogeneity] = WithName(unitInterval, BlockHomogeneity),
            [TransitionCompliance] = WithName(unitInterval, TransitionCompliance),
            [ShiftTypeRotation] = WithName(unitInterval, ShiftTypeRotation),
            [PreferredShiftFraction] = WithName(unitInterval, PreferredShiftFraction),
        };
    }

    public static LinguisticVariable BuildOutput()
    {
        return new LinguisticVariable(HarmonyScore, new Dictionary<string, MembershipFunction>
        {
            [OutputPoor] = new TrapezoidMf(0, 0, 0.1, 0.3),
            [OutputMediocre] = new TriangularMf(0.2, 0.4, 0.6),
            [OutputGood] = new TriangularMf(0.5, 0.7, 0.85),
            [OutputExcellent] = new TrapezoidMf(0.8, 0.9, 1, 1),
        });
    }

    private static LinguisticVariable BuildUnitIntervalVariable()
    {
        return new LinguisticVariable("UnitInterval", new Dictionary<string, MembershipFunction>
        {
            [TermLow] = new TrapezoidMf(0, 0, 0.2, 0.4),
            [TermMedium] = new TriangularMf(0.3, 0.5, 0.7),
            [TermHigh] = new TrapezoidMf(0.6, 0.8, 1, 1),
        });
    }

    private static LinguisticVariable WithName(LinguisticVariable template, string name)
    {
        return new LinguisticVariable(name, template.Terms);
    }
}
