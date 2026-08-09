// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Constraints;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Constraints;

/// <summary>
/// Wizard-1 adapter over the engine-neutral <see cref="PlanConstraintChecker"/>. Projects the
/// CoreScenario token genome onto <see cref="AssignmentView"/> and delegates the detection. The
/// count is the Stage-0 fitness value (must be minimised to zero for any feasible solution).
/// Behaviour is identical to the former in-class checker; the rules now live in PlanConstraintChecker
/// so the composite objective can run the same gate against a Harmonizer bitmap.
/// </summary>
public sealed class TokenConstraintChecker
{
    private readonly PlanConstraintChecker _planChecker = new();

    public IReadOnlyList<ConstraintViolation> Check(CoreScenario scenario, CoreWizardContext context)
        => _planChecker.Check(Project(scenario), context);

    public int CountViolations(CoreScenario scenario, CoreWizardContext context)
        => Check(scenario, context).Count;

    /// <summary>
    /// Stage-0 count split into the legality share (every kind except UnderSupply) and the total.
    /// The split lets the fitness order plans legal-first: an open slot is a smaller defect than a
    /// broken work rule.
    /// </summary>
    public (int Legality, int Total) CountViolationsSplit(CoreScenario scenario, CoreWizardContext context)
    {
        var violations = Check(scenario, context);
        var underSupply = 0;
        foreach (var violation in violations)
        {
            if (violation.Kind == ViolationKind.UnderSupply)
            {
                underSupply++;
            }
        }

        return (violations.Count - underSupply, violations.Count);
    }

    private static List<AssignmentView> Project(CoreScenario scenario)
    {
        var views = new List<AssignmentView>(scenario.Tokens.Count);
        foreach (var token in scenario.Tokens)
        {
            views.Add(AssignmentView.FromToken(token));
        }

        return views;
    }
}
