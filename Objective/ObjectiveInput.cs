// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Constraints;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Objective;

/// <summary>
/// Engine-agnostic input to <see cref="CompositeObjective"/>. The mutable plan is the flat
/// <see cref="AssignmentView"/> list (work assignments only — breaks are NOT assignments, they enter
/// via <see cref="BreakHoursByAgent"/>); the static constraint world is the <see cref="CoreWizardContext"/>;
/// the hard violations are precomputed by the adapter via <see cref="PlanConstraintChecker"/> so the
/// objective stays a pure scorer. Wizard-1 and the Harmonizer/W4 bitmap each adapt onto this shape
/// (see <see cref="ObjectiveInputBuilder"/>).
/// </summary>
/// <param name="Assignments">All work assignments of the plan being scored</param>
/// <param name="BreakHoursByAgent">Paid break hours per agent in-period (count toward target hours, never toward caps)</param>
/// <param name="Context">Static scheduling context (agents, contracts, shifts, eligibility, preferences)</param>
/// <param name="Violations">Hard-constraint violations of the plan, precomputed from Assignments + Context</param>
public sealed record ObjectiveInput(
    IReadOnlyList<AssignmentView> Assignments,
    IReadOnlyDictionary<string, double> BreakHoursByAgent,
    CoreWizardContext Context,
    IReadOnlyList<ConstraintViolation> Violations);
