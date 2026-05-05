// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Constraints;
using Klacks.ScheduleOptimizer.TokenEvolution;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

/// <summary>
/// Composite fitness evaluator that runs all 5 stages (Stage0 hard-violations, Stage1 guaranteed-hours lex,
/// Stage2 full-time decay, Stage3 soft constraints, Stage4 cosmetic) and fills the corresponding
/// properties on the scenario. Implements <see cref="IComparer{T}"/> for tournament selection in the GA loop.
/// </summary>
/// <param name="constraintChecker">Hard-constraint checker supplying Stage-0</param>
public sealed class TokenFitnessEvaluator : IComparer<CoreScenario>
{
    private readonly TokenConstraintChecker _constraintChecker;
    private readonly IReadOnlyDictionary<string, double> _maxPossiblePerAgent;
    private readonly IReadOnlyList<string> _agentsInPriorityOrder;

    /// <summary>Stage-2 exponential decay factor, autoresearch-trainable.</summary>
    public double Stage2Decay { get; init; } = 0.7;

    /// <summary>Stage-3 block-ordering weight.</summary>
    public double Stage3BlockOrderWeight { get; init; } = 0.4;

    /// <summary>Stage-3 shift-preference blacklist weight.</summary>
    public double Stage3BlacklistWeight { get; init; } = 0.3;

    /// <summary>Stage-3 location-continuity weight.</summary>
    public double Stage3LocationWeight { get; init; } = 0.2;

    /// <summary>Stage-3 max-optimal-gap weight.</summary>
    public double Stage3MaxGapWeight { get; init; } = 0.1;

    public TokenFitnessEvaluator(
        TokenConstraintChecker constraintChecker,
        IReadOnlyDictionary<string, double> maxPossiblePerAgent,
        IReadOnlyList<CoreAgent> agentsInPriorityOrder)
    {
        _constraintChecker = constraintChecker;
        _maxPossiblePerAgent = maxPossiblePerAgent;
        _agentsInPriorityOrder = agentsInPriorityOrder.Select(a => a.Id).ToList();
    }

    public static TokenFitnessEvaluator Create(CoreWizardContext context, TokenEvolutionConfig? config = null)
    {
        var cfg = config ?? new TokenEvolutionConfig();
        var checker = new TokenConstraintChecker();
        var maxPossible = new MaxPossibleCalculator().ComputeForAll(context);
        var priorityOrder = context.Agents
            .OrderByDescending(a => a.FullTime)
            .ThenByDescending(a => a.FullTime - a.CurrentHours)
            .ToList();
        return new TokenFitnessEvaluator(checker, maxPossible, priorityOrder)
        {
            Stage2Decay = cfg.FitnessStage2Decay,
            Stage3BlockOrderWeight = cfg.FitnessStage3BlockOrder,
            Stage3BlacklistWeight = cfg.FitnessStage3Blacklist,
            Stage3LocationWeight = cfg.FitnessStage3Location,
            Stage3MaxGapWeight = cfg.FitnessStage3MaxGap,
        };
    }

    public void Evaluate(CoreScenario scenario, CoreWizardContext context)
    {
        scenario.FitnessStage0 = _constraintChecker.CountViolations(scenario, context);
        var (stage1Flags, stage1Completion) = ComputeStage1(scenario, context);
        scenario.FitnessStage1 = stage1Completion;
        scenario.FitnessStage2 = ComputeStage2(scenario, context);
        scenario.FitnessStage3 = ComputeStage3(scenario, context);
        scenario.FitnessStage4 = ComputeStage4(scenario, context);

        // Back-compat aggregate fitness used by legacy code paths.
        scenario.Fitness = scenario.FitnessStage1
            + (scenario.FitnessStage2 * 0.1)
            + (scenario.FitnessStage3 * 0.01)
            + (scenario.FitnessStage4 * 0.001);
        scenario.HardViolations = scenario.FitnessStage0;
    }

    public int Compare(CoreScenario? x, CoreScenario? y)
    {
        if (x is null && y is null)
        {
            return 0;
        }

        if (x is null)
        {
            return 1;
        }

        if (y is null)
        {
            return -1;
        }

        var stage0 = x.FitnessStage0.CompareTo(y.FitnessStage0);
        if (stage0 != 0)
        {
            return stage0;
        }

        var stage1 = y.FitnessStage1.CompareTo(x.FitnessStage1);
        if (stage1 != 0)
        {
            return stage1;
        }

        var stage2 = y.FitnessStage2.CompareTo(x.FitnessStage2);
        if (stage2 != 0)
        {
            return stage2;
        }

        var stage3 = y.FitnessStage3.CompareTo(x.FitnessStage3);
        if (stage3 != 0)
        {
            return stage3;
        }

        return y.FitnessStage4.CompareTo(x.FitnessStage4);
    }

    private (IReadOnlyList<int> Flags, double CompletionRate) ComputeStage1(CoreScenario scenario, CoreWizardContext context)
    {
        var flags = new List<int>(_agentsInPriorityOrder.Count);
        var tokensByAgent = scenario.Tokens
            .GroupBy(t => t.AgentId)
            .ToDictionary(g => g.Key, g => g.Sum(t => (double)t.TotalHours));
        var breakHoursByAgent = ComputeBreakHoursByAgent(context);
        var agentLookup = context.Agents.ToDictionary(a => a.Id);

        foreach (var agentId in _agentsInPriorityOrder)
        {
            if (!agentLookup.TryGetValue(agentId, out var agent))
            {
                continue;
            }

            if (agent.GuaranteedHours <= 0)
            {
                continue;
            }

            var breakHours = breakHoursByAgent.GetValueOrDefault(agentId, 0);
            var maxPossible = _maxPossiblePerAgent.GetValueOrDefault(agentId, 0);
            if (agent.GuaranteedHours > maxPossible + agent.CurrentHours + breakHours)
            {
                flags.Add(1);
                continue;
            }

            var covered = agent.CurrentHours
                + tokensByAgent.GetValueOrDefault(agentId, 0)
                + breakHours;
            flags.Add(covered >= agent.GuaranteedHours ? 1 : 0);
        }

        var completion = flags.Count == 0 ? 1.0 : flags.Sum() / (double)flags.Count;
        return (flags, completion);
    }

    /// <summary>
    /// Sums Break.WorkTime for every break blocker per agent inside the period.
    /// Break hours count toward target hours (Stage 1/2/4) but NEVER toward MaxWeeklyHours.
    /// </summary>
    private static Dictionary<string, double> ComputeBreakHoursByAgent(CoreWizardContext context)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        if (context.BreakBlockers.Count == 0)
        {
            return result;
        }

        foreach (var blocker in context.BreakBlockers)
        {
            if (blocker.Hours <= 0m)
            {
                continue;
            }

            var fromDate = blocker.FromInclusive < context.PeriodFrom ? context.PeriodFrom : blocker.FromInclusive;
            var untilDate = blocker.UntilInclusive > context.PeriodUntil ? context.PeriodUntil : blocker.UntilInclusive;
            if (untilDate < fromDate)
            {
                continue;
            }

            var dayCount = untilDate.DayNumber - fromDate.DayNumber + 1;
            var totalHours = (double)blocker.Hours * dayCount;
            result[blocker.AgentId] = result.TryGetValue(blocker.AgentId, out var existing)
                ? existing + totalHours
                : totalHours;
        }

        return result;
    }

    private double ComputeStage2(CoreScenario scenario, CoreWizardContext context)
    {
        if (_agentsInPriorityOrder.Count == 0)
        {
            return 1;
        }

        var tokensByAgent = scenario.Tokens
            .GroupBy(t => t.AgentId)
            .ToDictionary(g => g.Key, g => g.Sum(t => (double)t.TotalHours));
        var breakHoursByAgent = ComputeBreakHoursByAgent(context);
        var agentLookup = context.Agents.ToDictionary(a => a.Id);

        double weightedScore = 0;
        double weightSum = 0;

        for (var i = 0; i < _agentsInPriorityOrder.Count; i++)
        {
            var agentId = _agentsInPriorityOrder[i];
            if (!agentLookup.TryGetValue(agentId, out var agent))
            {
                continue;
            }

            var weight = Math.Pow(Stage2Decay, i);
            weightSum += weight;

            var maxPossible = _maxPossiblePerAgent.GetValueOrDefault(agentId, 0);
            var target = agent.FullTime > 0 ? Math.Min(agent.FullTime, maxPossible) : maxPossible;
            double coverage;
            if (target <= 0)
            {
                coverage = 1;
            }
            else
            {
                var covered = agent.CurrentHours
                    + tokensByAgent.GetValueOrDefault(agentId, 0)
                    + breakHoursByAgent.GetValueOrDefault(agentId, 0);
                coverage = Math.Min(covered, target) / target;
            }

            weightedScore += weight * coverage;
        }

        return weightSum > 0 ? weightedScore / weightSum : 0;
    }

    private double ComputeStage3(CoreScenario scenario, CoreWizardContext context)
    {
        var blockOrder = ComputeBlockOrderingScore(scenario);
        var blacklist = ComputeBlacklistScore(scenario, context);
        var location = ComputeLocationContinuityScore(scenario);
        var gap = ComputeMaxOptimalGapScore(scenario, context);

        var totalWeight = Stage3BlockOrderWeight + Stage3BlacklistWeight + Stage3LocationWeight + Stage3MaxGapWeight;
        if (totalWeight <= 0)
        {
            return 0;
        }

        return (blockOrder * Stage3BlockOrderWeight
                + blacklist * Stage3BlacklistWeight
                + location * Stage3LocationWeight
                + gap * Stage3MaxGapWeight) / totalWeight;
    }

    private static double ComputeBlockOrderingScore(CoreScenario scenario)
    {
        var pairs = 0;
        var descending = 0;
        foreach (var block in scenario.Tokens.GroupBy(t => t.BlockId))
        {
            var ordered = block.OrderBy(t => t.StartAt).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                pairs++;
                if (ordered[i].ShiftTypeIndex < ordered[i - 1].ShiftTypeIndex)
                {
                    descending++;
                }
            }
        }

        return pairs == 0 ? 1 : 1.0 - (descending / (double)pairs);
    }

    private static double ComputeBlacklistScore(CoreScenario scenario, CoreWizardContext context)
    {
        if (scenario.Tokens.Count == 0)
        {
            return 1;
        }

        var blacklisted = scenario.Tokens.Count(t => context.ShiftPreferences
            .Any(p => p.AgentId == t.AgentId && p.ShiftRefId == t.ShiftRefId && p.Kind == ShiftPreferenceKind.Blacklist));

        return 1.0 - (blacklisted / (double)scenario.Tokens.Count);
    }

    private static double ComputeLocationContinuityScore(CoreScenario scenario)
    {
        var pairs = 0;
        var switches = 0;
        foreach (var perAgent in scenario.Tokens.GroupBy(t => t.AgentId))
        {
            var ordered = perAgent.OrderBy(t => t.StartAt).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                pairs++;
                if (ordered[i].LocationContext != ordered[i - 1].LocationContext)
                {
                    switches++;
                }
            }
        }

        return pairs == 0 ? 1 : 1.0 - (switches / (double)pairs);
    }

    private static double ComputeMaxOptimalGapScore(CoreScenario scenario, CoreWizardContext context)
    {
        var cap = context.SchedulingMaxOptimalGap;
        if (cap <= 0)
        {
            return 1;
        }

        var pairs = 0;
        var violations = 0;
        foreach (var grouped in scenario.Tokens.GroupBy(t => (t.AgentId, t.Date)))
        {
            var ordered = grouped.OrderBy(t => t.StartAt).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                pairs++;
                var gapHours = (ordered[i].StartAt - ordered[i - 1].EndAt).TotalHours;
                if (gapHours > cap)
                {
                    violations++;
                }
            }
        }

        return pairs == 0 ? 1 : 1.0 - (violations / (double)pairs);
    }

    private double ComputeStage4(CoreScenario scenario, CoreWizardContext context)
    {
        if (_agentsInPriorityOrder.Count == 0)
        {
            return 1;
        }

        var fairness = ComputeFairnessScore(scenario, context);
        var minimum = ComputeMinimumHoursScore(scenario, context);
        var symmetry = ComputeBlockSymmetryScore(scenario);

        return (fairness + minimum + symmetry) / 3.0;
    }

    private double ComputeFairnessScore(CoreScenario scenario, CoreWizardContext context)
    {
        var agentLookupIds = _agentsInPriorityOrder;
        if (agentLookupIds.Count == 0)
        {
            return 1;
        }

        var ratios = new List<double>();
        var tokensByAgent = scenario.Tokens
            .GroupBy(t => t.AgentId)
            .ToDictionary(g => g.Key, g => g.Sum(t => (double)t.TotalHours));
        var breakHoursByAgent = ComputeBreakHoursByAgent(context);

        foreach (var agentId in agentLookupIds)
        {
            var max = _maxPossiblePerAgent.GetValueOrDefault(agentId, 0);
            if (max <= 0)
            {
                continue;
            }

            var covered = tokensByAgent.GetValueOrDefault(agentId, 0)
                + breakHoursByAgent.GetValueOrDefault(agentId, 0);
            ratios.Add(Math.Min(1, covered / max));
        }

        if (ratios.Count == 0)
        {
            return 1;
        }

        var mean = ratios.Average();
        var variance = ratios.Sum(r => Math.Pow(r - mean, 2)) / ratios.Count;
        return 1.0 - Math.Min(1, Math.Sqrt(variance));
    }

    private double ComputeMinimumHoursScore(CoreScenario scenario, CoreWizardContext context)
    {
        var tokensByAgent = scenario.Tokens
            .GroupBy(t => t.AgentId)
            .ToDictionary(g => g.Key, g => g.Sum(t => (double)t.TotalHours));
        var breakHoursByAgent = ComputeBreakHoursByAgent(context);
        var agentLookup = context.Agents.ToDictionary(a => a.Id);

        double weightedScore = 0;
        double weightSum = 0;

        for (var i = 0; i < _agentsInPriorityOrder.Count; i++)
        {
            var agentId = _agentsInPriorityOrder[i];
            if (!agentLookup.TryGetValue(agentId, out var agent) || agent.MinimumHours <= 0)
            {
                continue;
            }

            var weight = Math.Pow(Stage2Decay, i);
            weightSum += weight;

            var actual = agent.CurrentHours
                + tokensByAgent.GetValueOrDefault(agentId, 0)
                + breakHoursByAgent.GetValueOrDefault(agentId, 0);
            var coverage = Math.Min(1, actual / agent.MinimumHours);
            weightedScore += weight * coverage;
        }

        return weightSum > 0 ? weightedScore / weightSum : 1;
    }

    private static double ComputeBlockSymmetryScore(CoreScenario scenario)
    {
        var lengths = scenario.Tokens
            .GroupBy(t => t.BlockId)
            .Select(g => g.Select(t => t.Date).Distinct().Count())
            .ToList();

        if (lengths.Count <= 1)
        {
            return 1;
        }

        var mean = lengths.Average();
        var variance = lengths.Sum(l => Math.Pow(l - mean, 2)) / lengths.Count;
        return 1.0 - Math.Min(1, Math.Sqrt(variance) / 7.0);
    }
}
