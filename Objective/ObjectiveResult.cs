// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Objective;

/// <param name="Gate">Tiered feasibility gate (hard floors + soft supply counts)</param>
/// <param name="Scalar">Weighted [0,1] sum of the soft terms; ranks admissible candidates. Does NOT encode the gate — the gate/guards are applied separately by the runner.</param>
/// <param name="SubScores">The individual soft sub-scores that compose the scalar</param>
/// <param name="Diagnostics">Worst-off values for the no-regression guards</param>
/// <param name="ChurnRatio">Edit-distance to the incumbent plan; set only at the Api/scenario layer (null in-engine, where no incumbent link exists)</param>
public sealed record ObjectiveResult(
    GateResult Gate,
    double Scalar,
    ObjectiveSubScores SubScores,
    ObjectiveDiagnostics Diagnostics,
    double? ChurnRatio = null);
