// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Constraints;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Constraints;
using Klacks.ScheduleOptimizer.TokenEvolution;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

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
    private readonly object _kindEligibilityLock = new();
    private CoreWizardContext? _kindEligibilityContext;
    private IReadOnlyDictionary<int, IReadOnlyDictionary<string, int>>? _kindEligibleDays;
    private readonly object _carryInLock = new();
    private CoreWizardContext? _carryInContext;
    private DateOnly _carryInAnchor;
    private IReadOnlyList<CarryInPackage>? _carryInPackages;

    /// <summary>Stage-1 roster-rank decay: weights WHO reaches the guaranteed hours top-down. 1.0 = index-blind count (legacy), lower = top-roster satisfaction dominates. Autoresearch-trainable.</summary>
    public double Stage1RankDecay { get; init; } = 0.85;

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

    /// <summary>Stage-3 weight for holding the carried-in packages that are still open at the period start.</summary>
    public double Stage3CarryInWeight { get; init; } = 0.2;

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

        // context.Agents IS the canonical top-down roster priority order (user order or
        // guaranteed-hours reshaping, established by the context builder). Auction IndexBonus,
        // ReassignMutation and TokenRepair already rank by this list — the Stage2 decay weights
        // must follow the same order, not a private re-sort.
        return new TokenFitnessEvaluator(checker, maxPossible, context.Agents)
        {
            Stage1RankDecay = cfg.FitnessStage1RankDecay,
            Stage2Decay = cfg.FitnessStage2Decay,
            Stage3BlockOrderWeight = cfg.FitnessStage3BlockOrder,
            Stage3BlacklistWeight = cfg.FitnessStage3Blacklist,
            Stage3LocationWeight = cfg.FitnessStage3Location,
            Stage3MaxGapWeight = cfg.FitnessStage3MaxGap,
            Stage3CarryInWeight = cfg.FitnessStage3CarryIn,
        };
    }

    public void Evaluate(CoreScenario scenario, CoreWizardContext context)
    {
        var (legality, total) = _constraintChecker.CountViolationsSplit(scenario, context);
        scenario.FitnessStage0Legality = legality;
        scenario.FitnessStage0 = total;
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

    /// <summary>
    /// Runs the full evaluation and additionally exposes the Stage-3/Stage-4 component breakdown that
    /// <see cref="Evaluate"/> computes internally and discards. Intended for a single call on the winning
    /// individual at run-end — it re-runs the shared private component methods, so the hot
    /// <see cref="Evaluate"/> path stays untouched (no extra allocations per generation). The returned
    /// stage aggregates are taken verbatim from the scenario filled by <see cref="Evaluate"/>.
    /// </summary>
    public DetailedFitnessResult EvaluateDetailed(CoreScenario scenario, CoreWizardContext context)
    {
        Evaluate(scenario, context);

        var stage3 = new Stage3Components(
            BlockOrder: ComputeBlockOrderingScore(scenario, context),
            Blacklist: ComputeBlacklistScore(scenario, context),
            Location: ComputeLocationContinuityScore(scenario),
            MaxGap: ComputeMaxOptimalGapScore(scenario, context));

        var stage4 = new Stage4Components(
            Fairness: ComputeFairnessScore(scenario, context),
            MinimumHours: ComputeMinimumHoursScore(scenario, context),
            BlockSymmetry: ComputeBlockSymmetryScore(scenario),
            ShiftKindFairness: ComputeShiftKindFairnessScore(scenario, context));

        return new DetailedFitnessResult(
            Stage0: scenario.FitnessStage0,
            Stage1: scenario.FitnessStage1,
            Stage2: scenario.FitnessStage2,
            Stage3: scenario.FitnessStage3,
            Stage4: scenario.FitnessStage4,
            Stage3Components: stage3,
            Stage4Components: stage4);
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

        var legality = x.FitnessStage0Legality.CompareTo(y.FitnessStage0Legality);
        if (legality != 0)
        {
            return legality;
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
            .ToDictionary(g => g.Key, g => g.Sum(t => (double)(t.TotalHours + t.Surcharges)));
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

        // Roster-rank weighting (top-down rule): when supply cannot satisfy everyone, plans that
        // satisfy higher-roster agents must outrank plans that satisfy the same number of lower
        // ones. Stage1RankDecay = 1.0 restores the legacy index-blind count.
        if (flags.Count == 0)
        {
            return (flags, 1.0);
        }

        double weightedSum = 0;
        double weightTotal = 0;
        for (var i = 0; i < flags.Count; i++)
        {
            var weight = Math.Pow(Stage1RankDecay, i);
            weightedSum += weight * flags[i];
            weightTotal += weight;
        }

        return (flags, weightedSum / weightTotal);
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

                // Symmetric accuracy: overshoot is penalised like shortfall. Since every slot
                // must be filled (UnderSupply is a hard violation), surplus hours have to land
                // somewhere — the rank decay steers them to the bottom of the roster where the
                // weight is lowest, keeping the top of the roster accurate (top-down rule).
                coverage = covered <= target
                    ? covered / target
                    : Math.Max(0, 1 - ((covered - target) / target));
            }

            weightedScore += weight * coverage;
        }

        return weightSum > 0 ? weightedScore / weightSum : 0;
    }

    private double ComputeStage3(CoreScenario scenario, CoreWizardContext context)
    {
        var blockOrder = ComputeBlockOrderingScore(scenario, context);
        var blacklist = ComputeBlacklistScore(scenario, context);
        var location = ComputeLocationContinuityScore(scenario);
        var gap = ComputeMaxOptimalGapScore(scenario, context);

        // A context without an open carry-in package drops the term AND its weight, so the stage-3
        // value of such a run is bit-identical to the value before the term existed.
        var (anchor, packages) = ResolveCarryInPackages(context);
        var carryInWeight = packages.Count > 0 ? Stage3CarryInWeight : 0;
        var carryIn = carryInWeight > 0 ? ComputeCarryInContinuityScore(scenario, anchor, packages) : 0;

        var totalWeight = Stage3BlockOrderWeight + Stage3BlacklistWeight + Stage3LocationWeight
            + Stage3MaxGapWeight + carryInWeight;
        if (totalWeight <= 0)
        {
            return 0;
        }

        return (blockOrder * Stage3BlockOrderWeight
                + blacklist * Stage3BlacklistWeight
                + location * Stage3LocationWeight
                + gap * Stage3MaxGapWeight
                + (carryIn * carryInWeight)) / totalWeight;
    }

    /// <summary>
    /// Open carry-in packages of the context, detected once and cached: the detection reads only the
    /// fixed works, which do not change during a run, and the evaluator is called from many threads.
    /// </summary>
    private (DateOnly Anchor, IReadOnlyList<CarryInPackage> Packages) ResolveCarryInPackages(
        CoreWizardContext context)
    {
        lock (_carryInLock)
        {
            if (!ReferenceEquals(_carryInContext, context))
            {
                _carryInAnchor = CarryInContinuation.FirstPlannableDay(context);
                _carryInPackages = CarryInContinuation.Detect(context, _carryInAnchor);
                _carryInContext = context;
            }

            return (_carryInAnchor, _carryInPackages!);
        }
    }

    /// <summary>
    /// Share of the owed continuation days that the plan actually holds — same employee, same day,
    /// same order. Protects the constructed continuation against drift once the operators start
    /// moving tokens; it sits in stage 3 and therefore never outranks legality, coverage, the
    /// guaranteed hours or the top-down rule.
    /// </summary>
    private static double ComputeCarryInContinuityScore(
        CoreScenario scenario, DateOnly anchor, IReadOnlyList<CarryInPackage> packages)
    {
        var held = new HashSet<(string AgentId, DateOnly Date, Guid ShiftRefId)>();
        foreach (var token in scenario.Tokens)
        {
            held.Add((token.AgentId, token.Date, token.ShiftRefId));
        }

        var owed = 0;
        var kept = 0;
        foreach (var package in packages)
        {
            for (var offset = 0; offset < package.RemainingDays; offset++)
            {
                owed++;
                if (held.Contains((package.AgentId, anchor.AddDays(offset), package.ShiftRefId)))
                {
                    kept++;
                }
            }
        }

        return owed == 0 ? 1 : kept / (double)owed;
    }

    private const int ShiftTypeCycleLength = 3;
    private const double NonCycleRotationPenalty = 0.5;

    /// <summary>
    /// Scores the shift-type rotation rule (early → late → night). Blocks are maximal runs of
    /// consecutive working dates per agent — token BlockIds are NOT used because every token
    /// created by the auction, coverage strategy and repair carries its own fresh BlockId, which
    /// made the old per-BlockId grouping evaluate nothing. Inside a block, backwards transitions
    /// (late → early etc.) are violations. Across blocks the schedule must rotate: starting the
    /// next block with the same type as the previous block is a full violation, the cycle-next
    /// type is ideal, any other change costs half. Agents without PerformsShiftWork are exempt —
    /// they may only work day shifts and must not be punished for repeating them.
    /// </summary>
    private static double ComputeBlockOrderingScore(CoreScenario scenario, CoreWizardContext context)
    {
        var shiftWorkers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var agent in context.Agents)
        {
            if (agent.PerformsShiftWork)
            {
                shiftWorkers.Add(agent.Id);
            }
        }

        double units = 0;
        double violations = 0;

        foreach (var perAgent in scenario.Tokens.GroupBy(t => t.AgentId, StringComparer.Ordinal))
        {
            if (!shiftWorkers.Contains(perAgent.Key))
            {
                continue;
            }

            var ordered = perAgent.OrderBy(t => t.Date).ThenBy(t => t.StartAt).ToList();
            var blocks = new List<List<CoreToken>>();
            foreach (var token in ordered)
            {
                if (blocks.Count == 0 || token.Date.DayNumber - blocks[^1][^1].Date.DayNumber > 1)
                {
                    blocks.Add([]);
                }
                blocks[^1].Add(token);
            }

            foreach (var block in blocks)
            {
                for (var i = 1; i < block.Count; i++)
                {
                    units++;
                    if (block[i].ShiftTypeIndex < block[i - 1].ShiftTypeIndex)
                    {
                        violations++;
                    }
                }
            }

            for (var b = 1; b < blocks.Count; b++)
            {
                units++;
                var previousType = blocks[b - 1][0].ShiftTypeIndex;
                var nextType = blocks[b][0].ShiftTypeIndex;
                if (nextType == previousType)
                {
                    violations += 1.0;
                }
                else if (nextType != (previousType + 1) % ShiftTypeCycleLength)
                {
                    violations += NonCycleRotationPenalty;
                }
            }
        }

        return units == 0 ? 1 : 1.0 - (violations / units);
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
        => LocationContinuity.Score(scenario);

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
        var kindFairness = ComputeShiftKindFairnessScore(scenario, context);

        return (fairness + minimum + symmetry + kindFairness) / 4.0;
    }

    /// <summary>
    /// Evenness of each shift kind's distribution over the agents that may hold it at all: an agent's
    /// share is its token count of the kind divided by its eligible days (days with at least one
    /// non-banned slot of that kind), so an agent barred from nights never dilutes the night cohort
    /// and a part-time eligibility is weighted, not compared absolutely. Per kind the score is
    /// 1 - min(1, stddev of the shares); the result is the mean over the kinds that exist.
    /// </summary>
    internal double ComputeShiftKindFairnessScore(CoreScenario scenario, CoreWizardContext context)
    {
        var eligibleDaysByKind = GetKindEligibility(context);
        if (eligibleDaysByKind.Count == 0)
        {
            return 1;
        }

        var countsByKindAndAgent = new Dictionary<(int Kind, string AgentId), int>();
        foreach (var token in scenario.Tokens)
        {
            var key = (token.ShiftTypeIndex, token.AgentId);
            countsByKindAndAgent[key] = countsByKindAndAgent.GetValueOrDefault(key, 0) + 1;
        }

        double scoreSum = 0;
        var kinds = 0;
        foreach (var (kind, eligibleByAgent) in eligibleDaysByKind)
        {
            var shares = new List<double>();
            foreach (var (agentId, eligibleDays) in eligibleByAgent)
            {
                if (eligibleDays > 0)
                {
                    shares.Add(countsByKindAndAgent.GetValueOrDefault((kind, agentId), 0) / (double)eligibleDays);
                }
            }

            if (shares.Count < 2)
            {
                continue;
            }

            var mean = shares.Average();
            var variance = shares.Sum(s => Math.Pow(s - mean, 2)) / shares.Count;
            scoreSum += 1.0 - Math.Min(1, Math.Sqrt(variance));
            kinds++;
        }

        return kinds == 0 ? 1 : scoreSum / kinds;
    }

    /// <summary>
    /// Eligible days per shift kind and agent, derived once per wizard context from the slot index and
    /// the ban list and cached by reference — Evaluate runs in parallel over one context, so the lazy
    /// initialisation is locked.
    /// </summary>
    private IReadOnlyDictionary<int, IReadOnlyDictionary<string, int>> GetKindEligibility(CoreWizardContext context)
    {
        lock (_kindEligibilityLock)
        {
            if (ReferenceEquals(_kindEligibilityContext, context) && _kindEligibleDays is not null)
            {
                return _kindEligibleDays;
            }

            var daysByKindAndAgent = new Dictionary<int, Dictionary<string, HashSet<DateOnly>>>();
            foreach (var ((shiftRefId, date), slot) in EvaluationContext.For(context).SlotsByKey)
            {
                if (!TimeOnly.TryParse(slot.StartTime, out var start)
                    || !TimeOnly.TryParse(slot.EndTime, out var end))
                {
                    continue;
                }

                var kind = ShiftTypeInference.FromSpan(start, end);
                if (!daysByKindAndAgent.TryGetValue(kind, out var byAgent))
                {
                    byAgent = new Dictionary<string, HashSet<DateOnly>>(StringComparer.Ordinal);
                    daysByKindAndAgent[kind] = byAgent;
                }

                foreach (var agent in context.Agents)
                {
                    if (!context.IsEligible(agent.Id, shiftRefId, date))
                    {
                        continue;
                    }

                    if (!byAgent.TryGetValue(agent.Id, out var days))
                    {
                        days = [];
                        byAgent[agent.Id] = days;
                    }

                    days.Add(date);
                }
            }

            var result = new Dictionary<int, IReadOnlyDictionary<string, int>>();
            foreach (var (kind, byAgent) in daysByKindAndAgent)
            {
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var agent in context.Agents)
                {
                    counts[agent.Id] = byAgent.TryGetValue(agent.Id, out var days) ? days.Count : 0;
                }

                result[kind] = counts;
            }

            _kindEligibilityContext = context;
            _kindEligibleDays = result;
            return result;
        }
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
