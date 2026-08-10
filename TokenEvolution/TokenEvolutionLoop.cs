// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using System.Runtime.ExceptionServices;
using Klacks.ScheduleOptimizer.Constraints;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction;
using Klacks.ScheduleOptimizer.TokenEvolution.Constraints;
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

    private readonly TokenPopulationBuilder _populationBuilder;
    private readonly BlockCrossover _crossover;
    private readonly TokenSwapMutation _swap;
    private readonly BlockSplitMutation _split;
    private readonly BlockMergeMutation _merge;
    private readonly ReassignMutation _reassign;
    private readonly TokenRepair _repair;
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

    public CoreScenario Run(
        CoreWizardContext context,
        TokenEvolutionConfig config,
        IProgress<TokenEvolutionProgress>? progress = null,
        CancellationToken cancellationToken = default,
        Action<string>? trace = null)
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

                var child = rng.NextDouble() < config.CrossoverRate
                    ? _crossover.Apply(new TokenOperatorContext(p1, p2, context, rng))
                    : CloneScenario(p1);

                if (rng.NextDouble() < config.MutationRate)
                {
                    child = ApplyWeightedMutation(child, context, rng, config);
                }

                children.Add(child);
            }

            EvaluateAll(children, evaluator, context, parallelism, cancellationToken);
            next.AddRange(children);

            population = next;
            var currentBest = SelectBest(population, evaluator);

            var sweepStart = sw.ElapsedMilliseconds;
            var preSweep = currentBest;
            currentBest = RunCoverageSweep(currentBest, context, rng, evaluator, cancellationToken, trace);
            currentBest = RunTopDownHandover(currentBest, context, evaluator, handoverSeen, trace);
            currentBest = RunSurplusHoursReturn(currentBest, context, evaluator, surplusReturnSeen, trace);
            currentBest = RunShiftKindBalance(currentBest, context, evaluator, balanceSeen, trace);
            currentBest = RunObjectContinuityBalance(currentBest, context, evaluator, orderBalanceSeen, trace);

            // The handover and balance passes reshape blocks, which can open a legal fill for a slot
            // the first sweep had to skip — one more sweep catches it in the same generation.
            currentBest = RunCoverageSweep(currentBest, context, rng, evaluator, cancellationToken, trace);
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
        return best;
    }

    private CoreScenario ApplyWeightedMutation(
        CoreScenario child, CoreWizardContext context, Random rng, TokenEvolutionConfig config)
    {
        var total = config.MutationWeightSwap + config.MutationWeightSplit + config.MutationWeightMerge
                    + config.MutationWeightReassign + config.MutationWeightRepair;
        if (total <= 0)
        {
            return child;
        }

        var pick = rng.NextDouble() * total;
        var cumulative = 0.0;

        cumulative += config.MutationWeightSwap;
        if (pick < cumulative)
        {
            return _swap.Apply(new TokenOperatorContext(child, null, context, rng));
        }

        cumulative += config.MutationWeightSplit;
        if (pick < cumulative)
        {
            return _split.Apply(new TokenOperatorContext(child, null, context, rng));
        }

        cumulative += config.MutationWeightMerge;
        if (pick < cumulative)
        {
            return _merge.Apply(new TokenOperatorContext(child, null, context, rng));
        }

        cumulative += config.MutationWeightReassign;
        if (pick < cumulative)
        {
            return _reassign.Apply(new TokenOperatorContext(child, null, context, rng));
        }

        return _repair.Apply(new TokenOperatorContext(child, null, context, rng));
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
    /// the handover the pass rewrites owners only, and it is kept only when it wins the lexicographic
    /// stage comparison. It carries its own gate on purpose: a return that the comparison rejects must
    /// not drag the useful handover result of the same generation down with it.
    /// </summary>
    /// <param name="scenario">Current best plan of the generation</param>
    /// <param name="context">Wizard context supplying the roster order and the rules</param>
    /// <param name="evaluator">Stage evaluator deciding whether the rebalanced plan is kept</param>
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

        var returned = _surplusReturn.Apply(scenario, context);
        if (ReferenceEquals(returned, scenario))
        {
            return scenario;
        }

        evaluator.Evaluate(returned, context);
        if (evaluator.Compare(returned, scenario) >= 0)
        {
            trace?.Invoke($"Run: surplus-hours return REJECTED by compare ({CountReowned(returned, scenario)} shifts offered)");
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
        Action<string>? trace = null)
    {
        var filled = _repair.FillAllUnderSupply(scenario, context, rng, cancellationToken, trace);
        if (filled.Tokens.Count != scenario.Tokens.Count)
        {
            evaluator.Evaluate(filled, context);
        }

        return filled;
    }
}
