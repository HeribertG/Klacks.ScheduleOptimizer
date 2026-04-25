// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Strategy that builds a single CoreScenario from the wizard context.
/// Implementations provide the initial population diversity (greedy vs random).
/// </summary>
public interface ITokenPopulationStrategy
{
    /// <summary>
    /// Produces one scenario candidate for the initial population.
    /// </summary>
    /// <param name="context">Wizard context with agents, shifts and constraints</param>
    /// <param name="rng">Random number generator for deterministic variation</param>
    CoreScenario BuildScenario(CoreWizardContext context, Random rng);
}
