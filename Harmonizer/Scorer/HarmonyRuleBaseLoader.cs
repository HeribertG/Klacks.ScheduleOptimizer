// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Common.Fuzzy;

namespace Klacks.ScheduleOptimizer.Harmonizer.Scorer;

/// <summary>
/// Loads the harmonizer's default fuzzy rule set from the embedded resource
/// `Klacks.ScheduleOptimizer.Harmonizer.Scorer.Resources.harmony-rules.json`.
/// </summary>
public static class HarmonyRuleBaseLoader
{
    private const string DefaultResourceName =
        "Klacks.ScheduleOptimizer.Harmonizer.Scorer.Resources.harmony-rules.json";

    public static IReadOnlyList<FuzzyRule> LoadDefault()
    {
        var assembly = typeof(HarmonyRuleBaseLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream(DefaultResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{DefaultResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return RuleBaseLoader.Parse(reader.ReadToEnd());
    }
}
