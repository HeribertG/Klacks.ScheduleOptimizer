// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Constraints.Rules;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Harmonizer.Rules;

/// <summary>
/// Hard planning-rule veto for bitmap moves with the delta semantics of the pre-commit check
/// (PlanningRuleFindingDelta): a move is rejected only when, for one hard rule and the receiving agent, the summed
/// Excess of the findings grows. Findings that already exist before the move (carry-in that meets a counter
/// threshold, a run the plan inherited) therefore never freeze the search; a move that keeps or reduces them passes.
/// Every hard rule reports per agent, so only the rows a move changes are compared. Fast path: when the row after the
/// move has no hard violation that involves a changed day (WouldViolate), no finding can have grown and the exact
/// per-agent comparison is skipped. The row is projected incrementally (BitmapRuleRuntime.ProjectIntoScratch): the
/// fast path reads neighbour days and whole counting periods, so it needs the full row, but only days whose cell
/// changed since the last projection of that agent are re-projected.
/// </summary>
/// <param name="runtime">Rule state of the run</param>
public sealed class PlanningRuleMoveGuard
{
    private const int StackRuleLimit = 32;

    private readonly BitmapRuleRuntime _runtime;

    public PlanningRuleMoveGuard(BitmapRuleRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
    }

    /// <summary>
    /// Null when replacing the given cells of one row creates or worsens no hard finding of the row's agent, otherwise
    /// a reason naming the rule kind and id (no display names).
    /// </summary>
    /// <param name="bitmap">Bitmap before the move</param>
    /// <param name="row">Receiving row</param>
    /// <param name="replacements">Days of the row and the cells they would hold after the move</param>
    /// <param name="roleLabel">Side label for the reason (rowA/rowB)</param>
    public string? Diagnose(HarmonyBitmap bitmap, int row, ReadOnlySpan<(int Day, Cell Cell)> replacements, string roleLabel)
    {
        var evaluator = _runtime.Evaluator;
        var ruleCount = evaluator.HardRuleCount;
        if (ruleCount == 0 || replacements.IsEmpty)
        {
            return null;
        }

        var agentIndex = _runtime.Projection.AgentIndexOf(bitmap.Rows[row]);
        if (agentIndex < 0)
        {
            return null;
        }

        var plan = _runtime.ProjectIntoScratch(bitmap, row, agentIndex);
        foreach (var (day, cell) in replacements)
        {
            _runtime.SetScratchDay(agentIndex, day, cell);
        }

        if (!InvolvesChangedDay(plan, agentIndex, replacements))
        {
            return null;
        }

        Span<decimal> after = ruleCount <= StackRuleLimit ? stackalloc decimal[ruleCount] : new decimal[ruleCount];
        evaluator.HardExcessOf(plan, agentIndex, after);
        foreach (var (day, _) in replacements)
        {
            _runtime.SetScratchDay(agentIndex, day, bitmap.GetCell(row, day));
        }

        Span<decimal> before = ruleCount <= StackRuleLimit ? stackalloc decimal[ruleCount] : new decimal[ruleCount];
        evaluator.HardExcessOf(plan, agentIndex, before);
        for (var i = 0; i < ruleCount; i++)
        {
            if (after[i] > before[i])
            {
                var rule = evaluator.HardRuleAt(i);
                return $"{roleLabel}: hard planning rule {rule.Kind} {rule.RuleId} would be violated (excess {before[i]} -> {after[i]})";
            }
        }

        return null;
    }

    private bool InvolvesChangedDay(RulePlan plan, int agentIndex, ReadOnlySpan<(int Day, Cell Cell)> replacements)
    {
        foreach (var (day, _) in replacements)
        {
            if (_runtime.Evaluator.WouldViolate(plan, agentIndex, day, plan.Get(agentIndex, day)))
            {
                return true;
            }
        }

        return false;
    }
}
