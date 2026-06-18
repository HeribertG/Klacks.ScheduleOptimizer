// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using System.Text.Json;
using Klacks.ScheduleOptimizer.Objective;

namespace Klacks.ScheduleOptimizer.Wizard4;

/// <summary>
/// Serialises a composite <see cref="ObjectiveResult"/> into the versioned, engine-tagged JSON stored
/// in AnalyseScenario.SubScoreJson at apply time. Versioned so the (deferred) preference-learner can
/// tell which dimensions were real in a given row as the objective matures, and engine-tagged so the
/// heterogeneous W1/W2/W3 score blobs can coexist in the same column.
/// </summary>
public static class ScenarioScoreSerializer
{
    public const int SchemaVersion = 1;
    public const string CompositeEngineTag = "composite";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Serialize(ObjectiveResult result)
    {
        var snapshot = new
        {
            v = SchemaVersion,
            engine = CompositeEngineTag,
            scalar = result.Scalar,
            gate = new
            {
                mandatoryQualMissing = result.Gate.MandatoryQualMissing,
                legality = result.Gate.Legality,
                underSupply = result.Gate.UnderSupply,
                overSupply = result.Gate.OverSupply,
            },
            subScores = new
            {
                fehler = result.SubScores.Fehler,
                stundenabgleich = result.SubScores.Stundenabgleich,
                praeferenzen = result.SubScores.Praeferenzen,
            },
            diagnostics = new
            {
                worstStundenabgleich = result.Diagnostics.WorstStundenabgleich,
                worstPraeferenzen = result.Diagnostics.WorstPraeferenzen,
                maxBlacklistFraction = result.Diagnostics.MaxBlacklistFraction,
            },
            churnRatio = result.ChurnRatio,
        };

        return JsonSerializer.Serialize(snapshot, Options);
    }
}
