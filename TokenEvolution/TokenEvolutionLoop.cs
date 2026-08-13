// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using System.Runtime.ExceptionServices;
using Klacks.ScheduleOptimizer.Constraints;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction;
using Klacks.ScheduleOptimizer.TokenEvolution.Constraints;
using Klacks.ScheduleOptimizer.TokenEvolution.Diagnostics;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;
using Klacks.ScheduleOptimizer.TokenEvolution.Operators;

namespace Klacks.ScheduleOptimizer.TokenEvolution;

/// <summary>
/// Genetic algorithm loop for token-based scheduling. Maintains a population, selects via tournament,
/// applies crossover + weighted mutation, preserves elites and reports progress per generation.
/// Runs a coverage-sweep on the best scenario after every generation to drive UnderSupply to zero
/// as long as it is theoretically reachable.
/// </summary>
public sealed class TokenEvolutionLoop
{
    private const int SequentialParallelism = 1;

    private const string CrossoverStage = "crossover";

    /// <summary>Origin label of a child cloned from one parent without crossover.</summary>
    private const string CloneOriginLabel = "clone";

    /// <summary>Origin label of a selected best that is no fresh child (elite, seed or injected).</summary>
    private const string CarriedOriginLabel = "carried";

    /// <summary>Joins the creation steps of one child into its origin chain, e.g. "crossover+mutation.swap".</summary>
    private const string OriginChainSeparator = "+";

    /// <summary>Label of the short-package count line the run emits for the returned best plan.</summary>
    private const string FinalPlanLabel = "final";
    private const string SweepBeforePassesStage = "sweep1";
    private const string HandoverStage = "handover";
    private const string SurplusReturnStage = "surplusReturn";
    private const string KindBalanceStage = "kindBalance";
    private const string OrderBalanceStage = "orderBalance";
    private const string SweepAfterPassesStage = "sweep2";

    private const string SwapStage = "mutation.swap";
    private const string SplitStage = "mutation.split";
    private const string MergeStage = "mutation.merge";
    private const string ReassignStage = "mutation.reassign";
    private const string RepairStage = "mutation.repair";
    private const string ConsolidateStage = "mutation.consolidate";
    private const string NoMutationStage = "mutation.none";

    private readonly TokenPopulationBuilder _populationBuilder;
    private readonly BlockCrossover _crossover;
    private readonly TokenSwapMutation _swap;
    private readonly BlockSplitMutation _split;
    private readonly BlockMergeMutation _merge;
    private readonly ReassignMutation _reassign;
    private readonly TokenRepair _repair;
    private readonly PackageConsolidationMutation _consolidate = new();
    private readonly TopDownHandover _handover = new();
    private readonly SurplusHoursReturn _surplusReturn = new();
    private readonly ShiftKindBalancer _kindBalancer = new();
    private readonly ObjectContinuityBalancer _orderBalancer = new();

    public TokenEvolutionLoop(
        TokenPopulationBuilder populationBuilder,
        BlockCrossover crossover,
        TokenSwapMutation swap,
        BlockSplitMutation split,
        BlockMergeMutation merge,
        ReassignMutation reassign,
        TokenRepair repair)
    {
        _populationBuilder = populationBuilder;
        _crossover = crossover;
        _swap = swap;
        _split = split;
        _merge = merge;
        _reassign = reassign;
        _repair = repair;
    }

    public static TokenEvolutionLoop Create(TokenConstraintChecker? checker = null)
    {
        var realChecker = checker ?? new TokenConstraintChecker();
        var builder = new TokenPopulationBuilder(
            new AuctionTokenStrategy(),
            new CoverageFirstTokenStrategy(),
            new GreedyTokenStrategy(),
            new RandomTokenStrategy(),
            new WarmStartTokenStrategy());
        return new TokenEvolutionLoop(
            builder,
            new BlockCrossover(),
            new TokenSwapMutation(),
            new BlockSplitMutation(),
            new BlockMergeMutation(),
            new ReassignMutation(),
            new TokenRepair(realChecker));
    }

    /// <summary>
    /// Evolves a plan for the given context.
    /// </summary>
    /// <param name="context">Wizard context: agents, shifts, rules and the works outside the genome</param>
    /// <param name="config">Population size, generation cap, seed, operator weights and budgets</param>
    /// <param name="progress">Optional per-generation progress sink</param>
    /// <param name="cancellationToken">Cancels the run between generations and inside the sweeps</param>
    /// <param name="trace">Optional textual trace of the run's phases and timings</param>
    /// <param name="blockDiagnostics">
    /// Optional sink for <see cref="OverlongBlockTrace"/>: when set, every step that produces a plan is
    /// compared with its input and each newly created overlong package is reported with the step that
    /// built it. Null keeps the whole comparison out of the run.
    /// </param>
    /// <param name="repairEscalations">
    /// Optional sink for the coverage escalation of <see cref="TokenRepair"/>: when set, every slot
    /// that only the widest rung of the ladder could staff is reported with the step that staffed it.
    /// The sink fires on a successful fill, never on a consultation, so a silent run proves the rung
    /// changed nothing. Null keeps the reporting out of the run.
    /// </param>
    /// <param name="packageDiagnostics">
    /// Optional sink for <see cref="ShortPackageTrace"/> (SPEC.md decision 13, seed-splintering
    /// diagnosis): when set, every step that produces a plan is compared with its input and each
    /// short package it creates ("+") or dissolves ("-") is reported with the step, the initial
    /// population and each generation's selected best are reported with their short-package count
    /// and the operator chain that produced the selected plan. Null keeps everything out of the run.
    /// </param>
    public CoreScenario Run(
        CoreWizardContext context,
        TokenEvolutionConfig config,
        IProgress<TokenEvolutionProgress>? progress = null,
        CancellationToken cancellationToken = default,
        Action<string>? trace = null,
        Action<string>? blockDiagnostics = null,
        Action<string>? repairEscalations = null,
        Action<string>? packageDiagnostics = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        trace?.Invoke($"Run: enter (population={config.PopulationSize}, maxGen={config.MaxGenerations}, agents={context.Agents.Count}, shifts={context.Shifts.Count}, lockedWorks={context.LockedWorks.Count})");

        var rng = new Random(config.RandomSeed);
        var evaluator = TokenFitnessEvaluator.Create(context, config);

        var t0 = sw.ElapsedMilliseconds;
        var population = _populationBuilder
            .BuildPopulation(context, config.PopulationSize, rng, cancellationToken, trace, config.InitAuctionRatio, config.InitWarmStartRatio)
            .ToList();
        trace?.Invoke($"Run: BuildPopulation done in {sw.ElapsedMilliseconds - t0}ms ({population.Count} scenarios)");
        if (blockDiagnostics is not null)
        {
            for (var index = 0; index < population.Count; index++)
            {
                OverlongBlockTrace.Report(blockDiagnostics, $"init[{index}]", null, population[index], context);
            }
        }

        // Provenance of freshly built children by scenario id, so the selected-best line can name
        // the operator chain that produced the plan. Only carried when the sink is active.
        var provenance = packageDiagnostics is null
            ? null
            : new Dictionary<string, string>(StringComparer.Ordinal);
        if (packageDiagnostics is not null)
        {
            for (var index = 0; index < population.Count; index++)
            {
                ShortPackageTrace.ReportCount(packageDiagnostics, $"init[{index}]", population[index], context);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        t0 = sw.ElapsedMilliseconds;
        EvaluationContext.For(context);
        var parallelism = ResolveParallelism(config);
        EvaluateAll(population, evaluator, context, parallelism, cancellationToken);
        trace?.Invoke($"Run: initial Evaluate done in {sw.ElapsedMilliseconds - t0}ms (parallelism {parallelism})");

        var best = SelectBest(population, evaluator);
        var generationsNoImprovement = 0;

        // The handover pass is deterministic, so running it twice on the same plan can only repeat
        // its own result. Elitism carries the same instance across generations; remembering which
        // plans were already offered keeps the pass off the hot path once the best plan is served.
        var handoverSeen = new HashSet<string>(StringComparer.Ordinal);
        var surplusReturnSeen = new HashSet<string>(StringComparer.Ordinal);
        var balanceSeen = new HashSet<string>(StringComparer.Ordinal);
        var orderBalanceSeen = new HashSet<string>(StringComparer.Ordinal);

        for (var generation = 1; generation <= config.MaxGenerations; generation++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (config.MaxRuntime is { } maxRuntime && sw.Elapsed >= maxRuntime)
            {
                trace?.Invoke($"Run: soft time budget of {maxRuntime.TotalSeconds:F0}s reached before gen={generation}; returning best so far");
                break;
            }

            var genStart = sw.ElapsedMilliseconds;
            var repairEscalationSink =
                RepairEscalationTrace.For(repairEscalations, generation, RepairStage);
            var sweepBeforeEscalationSink =
                RepairEscalationTrace.For(repairEscalations, generation, SweepBeforePassesStage);
            var sweepAfterEscalationSink =
                RepairEscalationTrace.For(repairEscalations, generation, SweepAfterPassesStage);

            var next = population
                .OrderBy(s => s, evaluator)
                .Take(config.ElitismCount)
                .ToList();

            var children = new List<CoreScenario>(Math.Max(0, config.PopulationSize - next.Count));
            while (next.Count + children.Count < config.PopulationSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var p1 = TournamentSelect(population, config.TournamentK, evaluator, rng);
                var p2 = TournamentSelect(population, config.TournamentK, evaluator, rng);

                var crossed = rng.NextDouble() < config.CrossoverRate;
                var child = crossed
                    ? _crossover.Apply(new TokenOperatorContext(p1, p2, context, rng))
                    : CloneScenario(p1);
                var origin = crossed ? CrossoverStage : CloneOriginLabel;
                if (crossed && blockDiagnostics is not null)
                {
                    OverlongBlockTrace.Report(
                        blockDiagnostics, $"gen{generation}.{CrossoverStage}", p1, p2, child, context);
                }

                if (crossed && packageDiagnostics is not null)
                {
                    ShortPackageTrace.Report(
                        packageDiagnostics, $"gen{generation}.{CrossoverStage}", p1, p2, child, context);
                }

                if (rng.NextDouble() < config.MutationRate)
                {
                    var parent = child;
                    var (mutated, operatorName) =
                        ApplyWeightedMutation(child, context, rng, config, repairEscalationSink);
                    child = mutated;
                    origin = origin + OriginChainSeparator + operatorName;
                    ReportOverlong(blockDiagnostics, generation, operatorName, parent, child, context);
                    ReportShortPackages(packageDiagnostics, generation, operatorName, parent, child, context);
                }

                if (provenance is not null)
                {
                    provenance[child.Id] = origin;
                }

                children.Add(child);
            }

            EvaluateAll(children, evaluator, context, parallelism, cancellationToken);
            next.AddRange(children);

            population = next;
            var currentBest = SelectBest(population, evaluator);
            if (packageDiagnostics is not null)
            {
                var selectedOrigin = provenance!.GetValueOrDefault(currentBest.Id, CarriedOriginLabel);
                ShortPackageTrace.ReportCount(
                    packageDiagnostics, $"gen{generation}.selected origin={selectedOrigin}", currentBest, context);
            }

            var sweepStart = sw.ElapsedMilliseconds;
            var preSweep = currentBest;
            var beforePass = currentBest;
            currentBest = RunCoverageSweep(
                currentBest, context, rng, evaluator, cancellationToken, trace, sweepBeforeEscalationSink);
            ReportOverlong(blockDiagnostics, generation, SweepBeforePassesStage, beforePass, currentBest, context);
            ReportShortPackages(packageDiagnostics, generation, SweepBeforePassesStage, beforePass, currentBest, context);

            beforePass = currentBest;
            currentBest = RunTopDownHandover(currentBest, context, evaluator, handoverSeen, trace);
            ReportOverlong(blockDiagnostics, generation, HandoverStage, beforePass, currentBest, context);
            ReportShortPackages(packageDiagnostics, generation, HandoverStage, beforePass, currentBest, context);

            beforePass = currentBest;
            currentBest = RunSurplusHoursReturn(currentBest, context, evaluator, surplusReturnSeen, trace);
            ReportOverlong(blockDiagnostics, generation, SurplusReturnStage, beforePass, currentBest, context);
            ReportShortPackages(packageDiagnostics, generation, SurplusReturnStage, beforePass, currentBest, context);

            beforePass = currentBest;
            currentBest = RunShiftKindBalance(currentBest, context, evaluator, balanceSeen, trace);
            ReportOverlong(blockDiagnostics, generation, KindBalanceStage, beforePass, currentBest, context);
            ReportShortPackages(packageDiagnostics, generation, KindBalanceStage, beforePass, currentBest, context);

            beforePass = currentBest;
            currentBest = RunObjectContinuityBalance(currentBest, context, evaluator, orderBalanceSeen, trace);
            ReportOverlong(blockDiagnostics, generation, OrderBalanceStage, beforePass, currentBest, context);
            ReportShortPackages(packageDiagnostics, generation, OrderBalanceStage, beforePass, currentBest, context);

            // The handover and balance passes reshape blocks, which can open a legal fill for a slot
            // the first sweep had to skip — one more sweep catches it in the same generation.
            beforePass = currentBest;
            currentBest = RunCoverageSweep(
                currentBest, context, rng, evaluator, cancellationToken, trace, sweepAfterEscalationSink);
            ReportOverlong(blockDiagnostics, generation, SweepAfterPassesStage, beforePass, currentBest, context);
            ReportShortPackages(packageDiagnostics, generation, SweepAfterPassesStage, beforePass, currentBest, context);
            if (!ReferenceEquals(currentBest, preSweep))
            {
                EliteInjector.ReplaceWorst(population, currentBest, evaluator);
                trace?.Invoke("Run: coverage-sweep winner injected into population");
            }
            var sweepMs = sw.ElapsedMilliseconds - sweepStart;

            if (evaluator.Compare(currentBest, best) < 0)
            {
                best = currentBest;
                generationsNoImprovement = 0;
            }
            else
            {
                generationsNoImprovement++;
            }

            var willStop = generationsNoImprovement >= config.EarlyStopNoImprovementGenerations
                           || generation == config.MaxGenerations;
            progress?.Report(new TokenEvolutionProgress(
                Generation: generation,
                MaxGenerations: config.MaxGenerations,
                BestHardViolations: best.FitnessStage0,
                BestStage1Completion: best.FitnessStage1,
                BestStage2Score: best.FitnessStage2,
                EarlyStopping: willStop));

            var genMs = sw.ElapsedMilliseconds - genStart;
            if (generation <= 3 || generation % 10 == 0 || willStop)
            {
                trace?.Invoke($"Run: gen={generation}/{config.MaxGenerations} took {genMs}ms (sweep={sweepMs}ms) tokens={best.Tokens.Count} stage1={best.FitnessStage1 * 100:F1}%");
            }

            if (generationsNoImprovement >= config.EarlyStopNoImprovementGenerations)
            {
                break;
            }
        }

        trace?.Invoke($"Run: total {sw.ElapsedMilliseconds}ms");
        ShortPackageTrace.ReportCount(packageDiagnostics, FinalPlanLabel, best, context);
        return best;
    }

    /// <summary>
    /// Applies one mutation drawn from the configured weights and names the operator that ran, so a
    /// diagnostics sink can attribute a structural defect to the operator instead of to the generation.
    /// </summary>
    /// <param name="child">Plan to mutate</param>
    /// <param name="context">Wizard context handed to the operator</param>
    /// <param name="rng">Random source of the run; the draw happens here and nowhere else</param>
    /// <param name="config">Supplies the six mutation weights</param>
    /// <param name="repairEscalations">Optional sink for the coverage escalation of the repair operator</param>
    private (CoreScenario Scenario, string Operator) ApplyWeightedMutation(
        CoreScenario child,
        CoreWizardContext context,
        Random rng,
        TokenEvolutionConfig config,
        Action<string>? repairEscalations)
    {
        var total = config.MutationWeightSwap + config.MutationWeightSplit + config.MutationWeightMerge
                    + config.MutationWeightReassign + config.MutationWeightRepair
                    + config.MutationWeightConsolidate;
        if (total <= 0)
        {
            return (child, NoMutationStage);
        }

        var pick = rng.NextDouble() * total;
        var cumulative = 0.0;

        cumulative += config.MutationWeightSwap;
        if (pick < cumulative)
        {
            return (_swap.Apply(new TokenOperatorContext(child, null, context, rng)), SwapStage);
        }

        cumulative += config.MutationWeightSplit;
        if (pick < cumulative)
        {
            return (_split.Apply(new TokenOperatorContext(child, null, context, rng)), SplitStage);
        }

        cumulative += config.MutationWeightMerge;
        if (pick < cumulative)
        {
            return (_merge.Apply(new TokenOperatorContext(child, null, context, rng)), MergeStage);
        }

        cumulative += config.MutationWeightReassign;
        if (pick < cumulative)
        {
            return (_reassign.Apply(new TokenOperatorContext(child, null, context, rng)), ReassignStage);
        }

        cumulative += config.MutationWeightRepair;
        if (pick < cumulative)
        {
            return (
                _repair.Apply(new TokenOperatorContext(child, null, context, rng), repairEscalations),
                RepairStage);
        }

        return (_consolidate.Apply(new TokenOperatorContext(child, null, context, rng)), ConsolidateStage);
    }

    /// <summary>
    /// Forwards one evolution step to <see cref="OverlongBlockTrace"/>. The stage name is only built
    /// when a sink exists, so a run without diagnostics pays a single null check per step.
    /// </summary>
    /// <param name="sink">Diagnostics sink; null skips everything</param>
    /// <param name="generation">Generation the step belongs to</param>
    /// <param name="pass">Name of the step</param>
    /// <param name="before">Plan the step started from</param>
    /// <param name="after">Plan the step produced</param>
    /// <param name="context">Wizard context supplying MaxWorkDays and the works outside the genome</param>
    private static void ReportOverlong(
        Action<string>? sink,
        int generation,
        string pass,
        CoreScenario before,
        CoreScenario after,
        CoreWizardContext context)
    {
        if (sink is null)
        {
            return;
        }

        OverlongBlockTrace.Report(sink, $"gen{generation}.{pass}", before, after, context);
    }

    private static void ReportShortPackages(
        Action<string>? sink,
        int generation,
        string pass,
        CoreScenario before,
        CoreScenario after,
        CoreWizardContext context)
    {
        if (sink is null)
        {
            return;
        }

        ShortPackageTrace.Report(sink, $"gen{generation}.{pass}", before, after, context);
    }

    private static CoreScenario TournamentSelect(
        IReadOnlyList<CoreScenario> population,
        int k,
        IComparer<CoreScenario> comparer,
        Random rng)
    {
        if (population.Count == 0)
        {
            throw new InvalidOperationException("Population must not be empty.");
        }

        var picks = Math.Min(k, population.Count);
        var best = population[rng.Next(population.Count)];
        for (var i = 1; i < picks; i++)
        {
            var candidate = population[rng.Next(population.Count)];
            if (comparer.Compare(candidate, best) < 0)
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Scores a set of scenarios, in parallel when the configuration allows it. The result is identical
    /// to a sequential run: every random draw happens in the breeding phase before this is called, the
    /// evaluation itself draws no randomness, and each scenario is written only by the thread that owns
    /// it. Tournament selection reads the PREVIOUS generation, so no child's fitness is read before this
    /// method returns.
    /// </summary>
    private static void EvaluateAll(
        IReadOnlyList<CoreScenario> scenarios,
        TokenFitnessEvaluator evaluator,
        CoreWizardContext context,
        int parallelism,
        CancellationToken cancellationToken)
    {
        if (parallelism <= SequentialParallelism || scenarios.Count <= SequentialParallelism)
        {
            foreach (var scenario in scenarios)
            {
                cancellationToken.ThrowIfCancellationRequested();
                evaluator.Evaluate(scenario, context);
            }

            return;
        }

        try
        {
            Parallel.ForEach(
                scenarios,
                new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = cancellationToken },
                scenario => evaluator.Evaluate(scenario, context));
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count == 1)
        {
            // Callers report the failure message to the user and the job store. Wrapping a single
            // evaluator error in "One or more errors occurred" would replace a diagnosable message with
            // a generic one purely because the loop happens to run in parallel.
            ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
        }
    }

    private static int ResolveParallelism(TokenEvolutionConfig config)
        => config.EvaluationParallelism > 0 ? config.EvaluationParallelism : Environment.ProcessorCount;

    private static CoreScenario SelectBest(IReadOnlyList<CoreScenario> population, IComparer<CoreScenario> comparer)
    {
        return population.OrderBy(s => s, comparer).First();
    }

    private static CoreScenario CloneScenario(CoreScenario source)
    {
        return new CoreScenario
        {
            Id = Guid.NewGuid().ToString(),
            Tokens = source.Tokens.ToList(),
            FitnessStage0 = source.FitnessStage0,
            FitnessStage1 = source.FitnessStage1,
            FitnessStage2 = source.FitnessStage2,
            FitnessStage3 = source.FitnessStage3,
            FitnessStage4 = source.FitnessStage4,
            Fitness = source.Fitness,
            HardViolations = source.HardViolations,
        };
    }

    /// <summary>
    /// Serves the guaranteed hours of the roster top-down on the current best plan (rule 5: the upper
    /// ranks are filled first, the last ones eat what remains). The pass rewrites owners only, so it
    /// cannot change coverage; it is still accepted only when it wins the lexicographic stage
    /// comparison, which keeps a plan that trades a hard or a higher-ranked stage away out of the run.
    /// </summary>
    /// <param name="scenario">Current best plan of the generation</param>
    /// <param name="context">Wizard context supplying the roster order and the rules</param>
    /// <param name="evaluator">Stage evaluator deciding whether the rebalanced plan is kept</param>
    /// <param name="alreadyOffered">Plans the pass has already seen; a deterministic pass repeated on the same plan cannot find anything new</param>
    /// <param name="trace">Optional trace sink</param>
    private CoreScenario RunTopDownHandover(
        CoreScenario scenario,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
        HashSet<string> alreadyOffered,
        Action<string>? trace = null)
    {
        if (!alreadyOffered.Add(scenario.Id))
        {
            return scenario;
        }

        var rebalanced = _handover.Apply(scenario, context);
        if (ReferenceEquals(rebalanced, scenario))
        {
            return scenario;
        }

        evaluator.Evaluate(rebalanced, context);
        if (evaluator.Compare(rebalanced, scenario) >= 0)
        {
            return scenario;
        }

        trace?.Invoke($"Run: top-down handover accepted (stage1={rebalanced.FitnessStage1:F4}, stage2={rebalanced.FitnessStage2:F4})");
        return rebalanced;
    }

    /// <summary>
    /// Hands surplus hours back down the roster on the current best plan (rule 5, the way back: an
    /// agent above its guarantee returns a shift to a worse rank that is still below its own). Like
    /// the handover the pass rewrites owners only. The acceptance gate sits inside the pass, on every
    /// single move: a bundle gate would let one rejected move discard all the legal ones with it, so
    /// what arrives here is already the product of moves that each won their own comparison.
    /// </summary>
    /// <param name="scenario">Current best plan of the generation</param>
    /// <param name="context">Wizard context supplying the roster order and the rules</param>
    /// <param name="evaluator">Stage evaluator gating every single return</param>
    /// <param name="alreadyOffered">Plans the pass has already seen; a deterministic pass repeated on the same plan cannot find anything new</param>
    /// <param name="trace">Optional trace sink</param>
    private CoreScenario RunSurplusHoursReturn(
        CoreScenario scenario,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
        HashSet<string> alreadyOffered,
        Action<string>? trace = null)
    {
        if (!alreadyOffered.Add(scenario.Id))
        {
            return scenario;
        }

        var returned = _surplusReturn.Apply(scenario, context, evaluator);
        if (ReferenceEquals(returned, scenario))
        {
            return scenario;
        }

        trace?.Invoke($"Run: surplus-hours return accepted ({CountReowned(returned, scenario)} shifts, stage1={returned.FitnessStage1:F4}, stage2={returned.FitnessStage2:F4})");
        return returned;
    }

    /// <summary>Number of tokens that changed owner between two plans of identical token order.</summary>
    private static int CountReowned(CoreScenario candidate, CoreScenario origin)
    {
        var count = 0;
        for (var i = 0; i < candidate.Tokens.Count && i < origin.Tokens.Count; i++)
        {
            if (!string.Equals(candidate.Tokens[i].AgentId, origin.Tokens[i].AgentId, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private CoreScenario RunShiftKindBalance(
        CoreScenario scenario,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
        HashSet<string> alreadyOffered,
        Action<string>? trace = null)
    {
        if (!alreadyOffered.Add(scenario.Id))
        {
            return scenario;
        }

        var balanced = _kindBalancer.Apply(scenario, context, evaluator);
        if (ReferenceEquals(balanced, scenario))
        {
            return scenario;
        }

        trace?.Invoke($"Run: shift-kind balance accepted (stage4={balanced.FitnessStage4:F4})");
        return balanced;
    }

    private CoreScenario RunObjectContinuityBalance(
        CoreScenario scenario,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
        HashSet<string> alreadyOffered,
        Action<string>? trace = null)
    {
        if (!alreadyOffered.Add(scenario.Id))
        {
            return scenario;
        }

        var balanced = _orderBalancer.Apply(scenario, context, evaluator);
        if (ReferenceEquals(balanced, scenario))
        {
            return scenario;
        }

        trace?.Invoke($"Run: order-continuity balance accepted (stage3={balanced.FitnessStage3:F4})");
        return balanced;
    }

    private CoreScenario RunCoverageSweep(
        CoreScenario scenario,
        CoreWizardContext context,
        Random rng,
        TokenFitnessEvaluator evaluator,
        CancellationToken cancellationToken = default,
        Action<string>? trace = null,
        Action<string>? escalations = null)
    {
        var filled = _repair.FillAllUnderSupply(
            scenario, context, rng, cancellationToken, trace, escalations);
        if (filled.Tokens.Count != scenario.Tokens.Count)
        {
            evaluator.Evaluate(filled, context);
        }

        return filled;
    }
}
