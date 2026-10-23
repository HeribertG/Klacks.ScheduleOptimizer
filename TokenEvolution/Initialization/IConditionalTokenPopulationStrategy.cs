// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Seeding strategy that only makes sense for some contexts. When it does not apply, the population builder gives it no
/// seats at all, so the population is built exactly as without the strategy.
/// </summary>
public interface IConditionalTokenPopulationStrategy : ITokenPopulationStrategy
{
    /// <summary>True when the strategy should seed scenarios for this context.</summary>
    /// <param name="context">Wizard context the population is built for</param>
    bool AppliesTo(CoreWizardContext context);
}
