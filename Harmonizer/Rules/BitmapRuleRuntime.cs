// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Constraints.Rules;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Harmonizer.Rules;

/// <summary>
/// Planning-rule state of one bitmap engine run: the rule context (BitmapInput agents with their night window and
/// workload, boundary assignments plus the loader's carry-in, the night minimum overlap of the API validators), the
/// incremental evaluator, the cell projection and one scratch plan the hard guard and the soft fitness term project
/// rows into. Built once per run and shared by the hooks of that run; not thread-safe.
/// </summary>
/// <param name="evaluator">Incremental evaluator bound to <paramref name="projection"/>'s context</param>
/// <param name="projection">Cell to rule-day projection of the run's input</param>
public sealed class BitmapRuleRuntime
{
    private BitmapRuleRuntime(IIncrementalPlanRuleEvaluator evaluator, BitmapRuleProjection projection, IReadOnlyList<PlanRule> rules)
    {
        Evaluator = evaluator;
        Projection = projection;
        Rules = rules;
        Scratch = new RulePlan(projection.Context);
    }

    public IIncrementalPlanRuleEvaluator Evaluator { get; }

    public BitmapRuleProjection Projection { get; }

    public IReadOnlyList<PlanRule> Rules { get; }

    /// <summary>Plan the hooks project single rows into; only the projected row is meaningful.</summary>
    public RulePlan Scratch { get; }

    public bool HasHardRules => Evaluator.HardRuleCount > 0;

    public bool HasSoftRules => Evaluator.HasSoftRules;

    /// <summary>Null when the input carries no planning rule - the caller then installs no hook at all.</summary>
    public static BitmapRuleRuntime? TryCreate(BitmapInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Rules is not { IsEmpty: false } rules)
        {
            return null;
        }

        var ignored = rules.IgnoredWorkIds ?? new HashSet<Guid>();
        var ruleInput = ignored.Count == 0
            ? input
            : input with { BoundaryAssignments = WithoutIgnored(input.BoundaryAssignments, ignored) };
        var context = RuleEvaluationContextFactory.FromBitmap(ruleInput, rules.NightRuleMinOverlapMinutes, rules.CarryIn);
        var evaluator = PlanRuleEvaluatorFactory.CreateIncremental(rules.Rules, context);
        return new BitmapRuleRuntime(evaluator, new BitmapRuleProjection(input, context, ignored), rules.Rules);
    }

    /// <summary>Plan-wide evaluation of the bitmap through the projection (reporting, tests, benchmark).</summary>
    public RuleEvaluation Evaluate(HarmonyBitmap bitmap) => Evaluator.Evaluate(Projection.Project(bitmap));

    private static IReadOnlyList<BitmapAssignment>? WithoutIgnored(IReadOnlyList<BitmapAssignment>? assignments, IReadOnlySet<Guid> ignored)
        => assignments?.Where(a => a.WorkIds.Count == 0 || a.WorkIds.Any(id => !ignored.Contains(id))).ToList();
}
