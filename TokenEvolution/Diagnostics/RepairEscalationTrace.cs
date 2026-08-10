// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution.Diagnostics;

/// <summary>
/// Names the evolution step that staffed a slot on the widest rung of the coverage escalation. The
/// repair operator knows which slot it had to escalate for but not which step invoked it, and the
/// loop knows the step but not the slot; this wrapper joins both into one attributable line. Only a
/// successful fill is reported, so a silent run is proof that the widest rung changed nothing — the
/// difference between "the ladder was consulted" and "the ladder fired", which matters because the
/// rung is consulted on every permanently unstaffable slot of every sweep.
/// </summary>
public static class RepairEscalationTrace
{
    private const string LinePrefix = "ESCALATED";

    private const string StageSuffix = ".escalated";

    /// <summary>
    /// Wraps a run's escalation sink with the producer label of one evolution step.
    /// </summary>
    /// <param name="sink">Escalation sink of the run; null returns null and allocates nothing</param>
    /// <param name="generation">Generation the step belongs to</param>
    /// <param name="stage">Name of the step that runs the repair operator</param>
    public static Action<string>? For(Action<string>? sink, int generation, string stage)
        => sink is null
            ? null
            : line => sink($"{LinePrefix} gen{generation}.{stage}{StageSuffix} {line}");
}
