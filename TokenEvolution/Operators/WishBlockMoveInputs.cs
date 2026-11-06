// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Constraints.Keywords;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// What every block move of one <see cref="WishBlockRepair"/> invocation reads, built once per invocation.
/// </summary>
/// <param name="Context">Wizard context supplying the roster and the rules</param>
/// <param name="Wishes">The positive and restricting keyword wishes of the context</param>
/// <param name="Continuation">Shifts of open carried-in packages; they never move</param>
/// <param name="RosterIndex">Position of each employee in the roster, used as the fixed candidate order</param>
/// <param name="Agents">The employees by id</param>
internal sealed record WishBlockMoveInputs(
    CoreWizardContext Context,
    WizardKeywordWishes Wishes,
    IReadOnlySet<(string AgentId, DateOnly Date, Guid ShiftRefId)> Continuation,
    IReadOnlyDictionary<string, int> RosterIndex,
    IReadOnlyDictionary<string, CoreAgent> Agents);
