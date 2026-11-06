// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// Result of one invocation of a keyword-wish repair (<see cref="WishRepairSwap"/>, <see cref="WishBlockRepair"/>).
/// </summary>
/// <param name="Scenario">The repaired plan, or the unchanged input when no move was accepted</param>
/// <param name="AcceptedMoves">Number of moves the invocation accepted; 0 when the input came back unchanged</param>
public sealed record WishRepairOutcome(CoreScenario Scenario, int AcceptedMoves);
