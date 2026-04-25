// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// A GA operator that transforms one or two scenarios into a new one.
/// Unary operators (mutation) set <see cref="TokenOperatorContext.Secondary"/> to null.
/// All operators MUST preserve locked tokens verbatim and propagate the AnalyseToken.
/// </summary>
public interface ITokenOperator
{
    CoreScenario Apply(TokenOperatorContext context);
}

/// <summary>
/// Operator input: the scenario(s) to transform, wizard context and a random source.
/// </summary>
/// <param name="Primary">Input scenario for mutation; first parent for crossover</param>
/// <param name="Secondary">Second parent for crossover; null for mutation</param>
/// <param name="Wizard">Wizard context (constraints, agents, shifts)</param>
/// <param name="Rng">Random number generator</param>
public sealed record TokenOperatorContext(
    CoreScenario Primary,
    CoreScenario? Secondary,
    CoreWizardContext Wizard,
    Random Rng);
