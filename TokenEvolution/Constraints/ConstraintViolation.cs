// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution.Constraints;

/// <summary>
/// A single hard-constraint violation recorded by the TokenConstraintChecker.
/// </summary>
/// <param name="Kind">The violation type</param>
/// <param name="AgentId">The agent owning the offending tokens (empty when slot-scoped)</param>
/// <param name="Date">Optional calendar date the violation is anchored to</param>
/// <param name="TokenBlockId">Optional block id for block-scoped violations</param>
/// <param name="Description">Human-readable diagnostic text</param>
/// <param name="ShiftRefId">Optional shift reference for slot-scoped violations (e.g. UnderSupply)</param>
public sealed record ConstraintViolation(
    ViolationKind Kind,
    string AgentId,
    DateOnly? Date,
    Guid? TokenBlockId,
    string Description,
    Guid? ShiftRefId = null);
