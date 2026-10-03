// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Builds a <see cref="RulePlan"/> from the engine plan shapes. In-period existing works are added only on
/// the Wizard-1 scenario path: there they are real works outside the genome, while on the bitmap paths they
/// already are cells, so adding them again would count them twice. FromBitmap works on merged cells (one
/// widened span per agent-day); FromBitmapInput keeps one segment per work and is therefore the exact path
/// for night and overlong-shift counting.
/// </summary>

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

public static class RulePlanFactory
{
    public static RulePlan FromAssignments(RuleEvaluationContext context, IReadOnlyList<AssignmentView> assignments)
    {
        var plan = new RulePlan(context);
        foreach (var assignment in assignments)
        {
            plan.TryAdd(RuleSegmentMapper.FromAssignment(assignment));
        }

        return plan;
    }

    public static RulePlan FromScenario(RuleEvaluationContext context, CoreScenario scenario, CoreWizardContext wizardContext)
    {
        var plan = new RulePlan(context);
        foreach (var token in scenario.Tokens)
        {
            plan.TryAdd(RuleSegmentMapper.FromAssignment(AssignmentView.FromToken(token)));
        }

        foreach (var work in wizardContext.ExistingWorkBlockers)
        {
            plan.TryAdd(RuleSegmentMapper.FromExistingWork(work));
        }

        return plan;
    }

    public static RulePlan FromBitmapInput(RuleEvaluationContext context, BitmapInput input)
    {
        var plan = new RulePlan(context);
        foreach (var assignment in input.Assignments)
        {
            if (RuleSegmentMapper.IsWorked(assignment.Symbol))
            {
                plan.TryAdd(RuleSegmentMapper.FromBitmapAssignment(assignment));
            }
        }

        return plan;
    }

    public static RulePlan FromBitmap(RuleEvaluationContext context, HarmonyBitmap bitmap)
    {
        var plan = new RulePlan(context);
        for (var row = 0; row < bitmap.RowCount; row++)
        {
            var agentId = bitmap.Rows[row].Id;
            for (var day = 0; day < bitmap.DayCount; day++)
            {
                var cell = bitmap.GetCell(row, day);
                if (RuleSegmentMapper.IsWorked(cell.Symbol))
                {
                    plan.TryAdd(RuleSegmentMapper.FromCell(agentId, bitmap.Days[day], cell));
                }
            }
        }

        return plan;
    }
}
