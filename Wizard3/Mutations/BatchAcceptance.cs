// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Wizard3.Mutations;

/// <summary>
/// Outcome category for a <see cref="MutationBatch"/> after evaluation by the BatchEvaluator.
/// </summary>
public enum BatchAcceptance : byte
{
    /// <summary>All steps passed hard constraints and the resulting score did not regress.</summary>
    Accepted = 0,

    /// <summary>A leading prefix of steps passed hard constraints; later steps failed; the
    /// prefix end-state did not regress the score, so the prefix was kept.</summary>
    PartiallyAccepted = 1,

    /// <summary>No step passed validation, or every prefix would have regressed the score.</summary>
    Rejected = 2,

    /// <summary>All steps were hard-constraint-valid but the end-state score is worse than
    /// the starting score. The whole batch was reverted (Score-Greedy guard).</summary>
    WouldDegrade = 3,
}
