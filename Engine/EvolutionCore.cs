// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Core evolution algorithm for shift scheduling optimization.
/// Port of evolution-core.ts — contains fitness calculation, mutation operators,
/// crossover, greedy scenario creation, and the main evolution loop.
/// </summary>
/// <param name="shifts">Available shifts to assign</param>
/// <param name="agents">Available agents (workers)</param>
/// <param name="config">Evolution configuration (population, generations, rates)</param>
/// <param name="penaltyWeights">Weights for fitness scoring</param>

using System.Diagnostics;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Engine;

public enum GreedyStrategy { Balanced, Deficit, Consistency }

public static class EvolutionCore
{
    public static EvolutionResult RunEvolution(
        List<CoreShift> shifts,
        List<CoreAgent> agents,
        CoreConfig config,
        CorePenaltyWeights penaltyWeights,
        Action<string>? onProgress = null)
    {
        var rng = config.RandomSeed.HasValue ? new SeededRng(config.RandomSeed.Value) : new SeededRng(42);
        Func<double> rngFn = rng.Next;

        var population = new List<CoreScenario>();
        var greedyCount = (int)(config.WarmStartRatio * config.PopulationSize);
        var randomCount = config.PopulationSize - greedyCount;
        var strategies = new[] { GreedyStrategy.Balanced, GreedyStrategy.Deficit, GreedyStrategy.Consistency };

        for (var i = 0; i < greedyCount; i++)
        {
            var variation = i / Math.Max(1.0, greedyCount - 1);
            var strategy = strategies[i % strategies.Length];
            var scenario = CreateGreedyScenario(shifts, agents, variation, rngFn, strategy);
            CalculateFitness(scenario, shifts, agents, penaltyWeights);
            population.Add(scenario);
        }

        for (var i = 0; i < randomCount; i++)
        {
            var scenario = CreateRandomScenario(shifts, agents, rngFn);
            CalculateFitness(scenario, shifts, agents, penaltyWeights);
            population.Add(scenario);
        }

        var sw = Stopwatch.StartNew();
        var stagnationCount = 0;
        var previousBestFitness = 0.0;
        var bestFitnessHistory = new List<double>();

        for (var gen = 1; gen <= config.MaxGenerations; gen++)
        {
            if (sw.ElapsedMilliseconds >= config.TimeLimitMs)
                return BuildResult(population, gen, "Time limit reached", "timeout", sw.ElapsedMilliseconds);

            population.Sort((a, b) => b.Fitness.CompareTo(a.Fitness));
            var bestFitness = population[0].Fitness;
            bestFitnessHistory.Add(bestFitness);

            if (bestFitness > previousBestFitness + config.ConvergenceThreshold)
            {
                stagnationCount = 0;
                previousBestFitness = bestFitness;
            }
            else
            {
                stagnationCount++;
            }

            if (gen % EvolutionConstants.PROGRESS_REPORT_INTERVAL == 0 || gen == 1)
                onProgress?.Invoke($"Gen {gen}/{config.MaxGenerations}: fitness={bestFitness:F4} coverage={population[0].Coverage:P0} stagnation={stagnationCount}");

            if (bestFitness >= config.TargetFitness)
                return BuildResult(population, gen, "Target fitness reached", "target", sw.ElapsedMilliseconds);

            if (stagnationCount >= config.StagnationLimit)
                return BuildResult(population, gen, $"Stagnation after {stagnationCount} generations", "stagnation", sw.ElapsedMilliseconds);

            if (bestFitnessHistory.Count >= EvolutionConstants.CONVERGENCE_HISTORY_SIZE)
            {
                var recent = bestFitnessHistory.TakeLast(EvolutionConstants.CONVERGENCE_HISTORY_SIZE).ToList();
                var improvement = recent[0] == 0 ? 1.0 : (recent[^1] - recent[0]) / recent[0];
                if (improvement < config.ConvergenceThreshold)
                    return BuildResult(population, gen, "Converged", "converged", sw.ElapsedMilliseconds);
            }

            var newPop = new List<CoreScenario>(population.Take(config.EliteCount));

            while (newPop.Count < config.PopulationSize)
            {
                var p1 = TournamentSelect(population, rngFn);
                var p2 = TournamentSelect(population, rngFn);

                CoreScenario child1, child2;
                if (rngFn() <= config.CrossoverRate)
                    (child1, child2) = CrossoverBlock(p1, p2, shifts, rngFn);
                else
                {
                    child1 = CloneScenario(p1, rngFn);
                    child2 = CloneScenario(p2, rngFn);
                }

                var m1 = Mutate(child1, shifts, agents, config, rngFn);
                var m2 = Mutate(child2, shifts, agents, config, rngFn);

                CalculateFitness(m1, shifts, agents, penaltyWeights);
                CalculateFitness(m2, shifts, agents, penaltyWeights);

                newPop.Add(m1);
                if (newPop.Count < config.PopulationSize) newPop.Add(m2);
            }

            population.Clear();
            population.AddRange(newPop);
        }

        return BuildResult(population, config.MaxGenerations, "Max generations reached", "maxgen", sw.ElapsedMilliseconds);
    }

    public static void CalculateFitness(
        CoreScenario scenario,
        List<CoreShift> shifts,
        List<CoreAgent> agents,
        CorePenaltyWeights weights)
    {
        var totalScore = 0.0;

        var assignedShiftIds = new HashSet<string>(scenario.Assignments.Select(a => a.ShiftId));
        var coveredCount = shifts.Count(s => assignedShiftIds.Contains(s.Id));
        var uncoveredCount = shifts.Count - coveredCount;
        totalScore += coveredCount * weights.CoverageBonus;
        totalScore += uncoveredCount * weights.UncoveredPenalty;

        var avgMotivation = scenario.Assignments.Count > 0
            ? scenario.Assignments.Average(a => a.MotivationScore)
            : 0.0;
        totalScore += avgMotivation * weights.MotivationBonus * scenario.Assignments.Count;

        if (agents.Count > 0 && scenario.Assignments.Count > 0)
        {
            var fairness = CalculateFairness(scenario, agents);
            totalScore += fairness * weights.FairnessBonus * agents.Count;
        }

        var hardCount = CountHardViolationsFast(scenario.Assignments, shifts, agents);
        totalScore += hardCount * weights.HardViolation;

        var softCount = CountSoftViolationsFast(scenario.Assignments, shifts, agents);
        totalScore += softCount * weights.SoftViolation;

        var maxPossible = shifts.Count * weights.CoverageBonus
            + weights.MotivationBonus * shifts.Count
            + weights.FairnessBonus * agents.Count
            - shifts.Count * weights.UncoveredPenalty;

        scenario.PenaltyScore = totalScore;
        scenario.HardViolations = hardCount;
        scenario.Fitness = maxPossible > 0 ? Math.Clamp(totalScore / maxPossible, 0, 1) : 0;
        scenario.Coverage = shifts.Count > 0 ? (double)coveredCount / shifts.Count : 1;
    }

    public static double CalculateFairness(CoreScenario scenario, List<CoreAgent> agents)
    {
        if (agents.Count == 0 || scenario.Assignments.Count == 0) return 1;

        var hoursPerAgent = new Dictionary<string, double>();
        foreach (var a in scenario.Assignments)
            hoursPerAgent[a.AgentId] = hoursPerAgent.GetValueOrDefault(a.AgentId) + 1;

        var hours = hoursPerAgent.Values.ToList();
        var avg = hours.Average();
        if (avg == 0) return 1;

        var cv = Math.Sqrt(hours.Average(h => Math.Pow(h - avg, 2))) / avg;
        return Math.Max(0, 1 - cv);
    }

    public static CoreScenario CreateRandomScenario(
        List<CoreShift> shifts,
        List<CoreAgent> agents,
        Func<double> rng)
    {
        var assignments = new List<CoreAssignment>();
        foreach (var shift in shifts)
        {
            if (rng() < SchedulingConstants.RANDOM_ASSIGNMENT_PROBABILITY)
            {
                var agent = agents[(int)(rng() * agents.Count)];
                assignments.Add(new CoreAssignment(
                    shift.Id,
                    agent.Id,
                    agent.Motivation * (AgentStateConstants.DEFAULT_SATISFACTION + rng() * AgentStateConstants.DEFAULT_SATISFACTION)));
            }
        }
        return new CoreScenario { Id = GenerateId(rng), Assignments = assignments };
    }

    public static CoreScenario CreateGreedyScenario(
        List<CoreShift> shifts,
        List<CoreAgent> agents,
        double variation,
        Func<double> rng,
        GreedyStrategy strategy = GreedyStrategy.Balanced)
    {
        var (deficitW, motivW, consistW, restPen) = strategy switch
        {
            GreedyStrategy.Deficit => (EvolutionConstants.GREEDY_HOUR_DEFICIT_WEIGHT * 3, EvolutionConstants.GREEDY_MOTIVATION_WEIGHT * 0.5, EvolutionConstants.GREEDY_BLOCK_CONSISTENCY_WEIGHT * 0.5, -300.0),
            GreedyStrategy.Consistency => (EvolutionConstants.GREEDY_HOUR_DEFICIT_WEIGHT, EvolutionConstants.GREEDY_MOTIVATION_WEIGHT * 0.5, EvolutionConstants.GREEDY_BLOCK_CONSISTENCY_WEIGHT * 4, -500.0),
            _ => (EvolutionConstants.GREEDY_HOUR_DEFICIT_WEIGHT, EvolutionConstants.GREEDY_MOTIVATION_WEIGHT, EvolutionConstants.GREEDY_BLOCK_CONSISTENCY_WEIGHT, -500.0),
        };
        var assignments = new List<CoreAssignment>();
        var agentScheduledHours = new Dictionary<string, double>();
        var agentDailyHours = new Dictionary<string, double>();
        var agentWeeklyHours = new Dictionary<string, double>();
        var agentDailySlots = new Dictionary<string, List<(string Start, string End)>>();
        var agentDailyShiftNames = new Dictionary<string, Dictionary<string, string>>();
        var agentLastEnd = new Dictionary<string, DateTime>();
        var agentWorkedDates = new Dictionary<string, SortedSet<string>>();

        var sortedShifts = shifts
            .OrderBy(s => s.Date)
            .ThenBy(s => s.StartTime)
            .ThenByDescending(s => s.Priority)
            .ToList();
        if (variation > 0)
        {
            for (var i = sortedShifts.Count - 1; i > 0; i--)
            {
                if (rng() < variation * EvolutionConstants.GREEDY_SHUFFLE_FACTOR)
                {
                    var j = (int)(rng() * (i + 1));
                    (sortedShifts[i], sortedShifts[j]) = (sortedShifts[j], sortedShifts[i]);
                }
            }
        }

        var sortedAgents = agents.OrderByDescending(a => a.GuaranteedHours).ToList();

        foreach (var shift in sortedShifts)
        {
            var dateKey = shift.Date.Split('T')[0];
            var weekKey = GetWeekKey(dateKey);
            CoreAgent? bestAgent = null;
            var bestScore = double.NegativeInfinity;

            foreach (var agent in sortedAgents)
            {
                var dailyKey = $"{agent.Id}_{dateKey}";
                var dailyHrs = agentDailyHours.GetValueOrDefault(dailyKey);
                if (dailyHrs + shift.Hours > agent.MaxDailyHours) continue;

                var wkKey = $"{agent.Id}_{weekKey}";
                var weeklyHrs = agentWeeklyHours.GetValueOrDefault(wkKey);
                if (weeklyHrs + shift.Hours > agent.MaxWeeklyHours) continue;

                if (agentDailySlots.TryGetValue(dailyKey, out var existingSlots))
                {
                    var overlaps = false;
                    foreach (var slot in existingSlots)
                    {
                        if (string.Compare(shift.StartTime, slot.End, StringComparison.Ordinal) < 0 &&
                            string.Compare(slot.Start, shift.EndTime, StringComparison.Ordinal) < 0)
                        {
                            overlaps = true;
                            break;
                        }
                    }
                    if (overlaps) continue;
                }

                double restPenalty = 0;
                if (agentLastEnd.TryGetValue(agent.Id, out var lastEnd))
                {
                    var currStart = DateTime.Parse($"{dateKey}T{shift.StartTime}");
                    var restHours = (currStart - lastEnd).TotalHours;
                    if (restHours > 0 && restHours < agent.MinRestHours)
                        restPenalty = restPen;
                }

                var totalHours = agentScheduledHours.GetValueOrDefault(agent.Id);
                var hourDeficit = agent.GuaranteedHours - (agent.CurrentHours + totalHours);
                var prevDayKey = GetPreviousDayKey(dateKey);

                string? prevShiftName = null;
                if (agentDailyShiftNames.TryGetValue(agent.Id, out var shiftNames))
                    shiftNames.TryGetValue(prevDayKey, out prevShiftName);

                double consecutivePenalty = 0;
                if (agentWorkedDates.TryGetValue(agent.Id, out var workedDates))
                {
                    var consecutive = CountConsecutiveDaysEnding(workedDates, dateKey);
                    if (consecutive >= agent.MaxConsecutiveDays && !workedDates.Contains(dateKey))
                        consecutivePenalty = -50;
                    else if (consecutive >= agent.MaxConsecutiveDays - 1 && !workedDates.Contains(dateKey))
                        consecutivePenalty = -20;
                }

                var score = hourDeficit * deficitW
                    + agent.Motivation * motivW
                    - totalHours
                    + (prevShiftName == shift.Name ? consistW : 0)
                    + consecutivePenalty
                    + restPenalty;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestAgent = agent;
                }
            }

            if (bestAgent is not null)
            {
                assignments.Add(new CoreAssignment(shift.Id, bestAgent.Id, bestAgent.Motivation));
                agentScheduledHours[bestAgent.Id] = agentScheduledHours.GetValueOrDefault(bestAgent.Id) + shift.Hours;

                var dailyKey = $"{bestAgent.Id}_{dateKey}";
                agentDailyHours[dailyKey] = agentDailyHours.GetValueOrDefault(dailyKey) + shift.Hours;

                var wkKey = $"{bestAgent.Id}_{weekKey}";
                agentWeeklyHours[wkKey] = agentWeeklyHours.GetValueOrDefault(wkKey) + shift.Hours;

                if (!agentDailySlots.ContainsKey(dailyKey))
                    agentDailySlots[dailyKey] = [];
                agentDailySlots[dailyKey].Add((shift.StartTime, shift.EndTime));

                if (!agentDailyShiftNames.ContainsKey(bestAgent.Id))
                    agentDailyShiftNames[bestAgent.Id] = [];
                agentDailyShiftNames[bestAgent.Id][dateKey] = shift.Name;

                var shiftEnd = DateTime.Parse($"{dateKey}T{shift.EndTime}");
                if (string.Compare(shift.EndTime, shift.StartTime, StringComparison.Ordinal) <= 0)
                    shiftEnd = shiftEnd.AddDays(1);
                if (!agentLastEnd.TryGetValue(bestAgent.Id, out var existEnd) || shiftEnd > existEnd)
                    agentLastEnd[bestAgent.Id] = shiftEnd;

                if (!agentWorkedDates.ContainsKey(bestAgent.Id))
                    agentWorkedDates[bestAgent.Id] = [];
                agentWorkedDates[bestAgent.Id].Add(dateKey);
            }
        }

        return new CoreScenario { Id = GenerateId(rng), Assignments = assignments };
    }

    private static int CountConsecutiveDaysEnding(SortedSet<string> workedDates, string targetDate)
    {
        var count = 0;
        var current = DateTime.Parse(targetDate).AddDays(-1);
        while (workedDates.Contains(current.ToString("yyyy-MM-dd")))
        {
            count++;
            current = current.AddDays(-1);
        }
        return count;
    }

    private static string GetWeekKey(string dateStr)
    {
        var d = DateTime.Parse(dateStr);
        var day = (int)d.DayOfWeek;
        var diff = day == 0 ? -6 : 1 - day;
        return d.AddDays(diff).ToString("yyyy-MM-dd");
    }

    public static CoreScenario Mutate(
        CoreScenario scenario,
        List<CoreShift> shifts,
        List<CoreAgent> agents,
        CoreConfig config,
        Func<double> rng)
    {
        if (rng() > config.MutationRate) return scenario;

        var newAssignments = new List<CoreAssignment>(scenario.Assignments);

        if (scenario.HardViolations > 0 && rng() < 0.5)
        {
            MutateFixViolation(newAssignments, shifts, agents, rng);
        }
        else
        {
            var roll = rng();
            if (roll < EvolutionConstants.MUTATION_SWAP_THRESHOLD && newAssignments.Count > 0)
                MutateSwap(newAssignments, agents, rng);
            else if (roll < EvolutionConstants.MUTATION_REMOVE_THRESHOLD && newAssignments.Count > 0)
                MutateRemove(newAssignments, rng);
            else if (roll < EvolutionConstants.MUTATION_REPAIR_THRESHOLD)
                MutateRepair(newAssignments, agents, rng);
            else
                MutateHungryFirst(newAssignments, shifts, agents, rng);
        }

        return new CoreScenario { Id = GenerateId(rng), Assignments = newAssignments };
    }

    private static void MutateFixViolation(
        List<CoreAssignment> assignments,
        List<CoreShift> shifts,
        List<CoreAgent> agents,
        Func<double> rng)
    {
        var violations = ConstraintEngine.EvaluateHardViolations(assignments, shifts, agents);
        if (violations.Count == 0) return;

        var shiftMap = shifts.ToDictionary(s => s.Id);
        var agentMap = agents.ToDictionary(a => a.Id);

        var violatingAgentIds = new HashSet<string>(violations.Select(v => v.AgentId));

        var violatingIndices = assignments
            .Select((a, i) => (a, i))
            .Where(x => violatingAgentIds.Contains(x.a.AgentId))
            .Select(x => x.i)
            .ToList();

        if (violatingIndices.Count == 0) return;

        var targetIdx = violatingIndices[(int)(rng() * violatingIndices.Count)];
        var targetAssignment = assignments[targetIdx];

        if (!shiftMap.TryGetValue(targetAssignment.ShiftId, out var targetShift)) return;

        var agentDailyHours = new Dictionary<string, double>();
        foreach (var a in assignments)
        {
            if (!shiftMap.TryGetValue(a.ShiftId, out var s)) continue;
            var key = $"{a.AgentId}_{s.Date.Split('T')[0]}";
            agentDailyHours[key] = agentDailyHours.GetValueOrDefault(key) + s.Hours;
        }

        var dateKey = targetShift.Date.Split('T')[0];
        var candidates = agents
            .Where(a => a.Id != targetAssignment.AgentId)
            .Where(a =>
            {
                var key = $"{a.Id}_{dateKey}";
                return agentDailyHours.GetValueOrDefault(key) + targetShift.Hours <= a.MaxDailyHours;
            })
            .ToList();

        if (candidates.Count > 0)
        {
            var newAgent = candidates[(int)(rng() * candidates.Count)];
            assignments[targetIdx] = targetAssignment with { AgentId = newAgent.Id, MotivationScore = newAgent.Motivation };
        }
        else
        {
            assignments.RemoveAt(targetIdx);
        }
    }

    private static void MutateSwap(List<CoreAssignment> assignments, List<CoreAgent> agents, Func<double> rng)
    {
        var idx = (int)(rng() * assignments.Count);
        var newAgent = agents[(int)(rng() * agents.Count)];
        assignments[idx] = assignments[idx] with { AgentId = newAgent.Id };
    }

    private static void MutateRemove(List<CoreAssignment> assignments, Func<double> rng)
    {
        assignments.RemoveAt((int)(rng() * assignments.Count));
    }

    private static void MutateRepair(List<CoreAssignment> assignments, List<CoreAgent> agents, Func<double> rng)
    {
        var agentCounts = new Dictionary<string, int>();
        foreach (var a in assignments)
            agentCounts[a.AgentId] = agentCounts.GetValueOrDefault(a.AgentId) + 1;

        var maxCount = 0;
        var overloaded = string.Empty;
        foreach (var (id, count) in agentCounts)
        {
            if (count > maxCount) { maxCount = count; overloaded = id; }
        }

        if (string.IsNullOrEmpty(overloaded)) return;

        var underloaded = agents
            .Where(a => agentCounts.GetValueOrDefault(a.Id) < maxCount && a.Id != overloaded)
            .OrderBy(a => agentCounts.GetValueOrDefault(a.Id))
            .ToList();

        if (underloaded.Count == 0) return;

        var targets = assignments
            .Select((a, i) => (a, i))
            .Where(x => x.a.AgentId == overloaded)
            .ToList();

        if (targets.Count == 0) return;

        var target = targets[(int)(rng() * targets.Count)];
        assignments[target.i] = assignments[target.i] with { AgentId = underloaded[0].Id };
    }

    private static void MutateHungryFirst(
        List<CoreAssignment> assignments,
        List<CoreShift> shifts,
        List<CoreAgent> agents,
        Func<double> rng)
    {
        var shiftMap = shifts.ToDictionary(s => s.Id);
        var agentHours = new Dictionary<string, double>();
        foreach (var a in assignments)
        {
            if (shiftMap.TryGetValue(a.ShiftId, out var s))
                agentHours[a.AgentId] = agentHours.GetValueOrDefault(a.AgentId) + s.Hours;
        }

        CoreAgent? hungriest = null;
        var maxDeficit = double.NegativeInfinity;
        foreach (var agent in agents)
        {
            var deficit = agent.GuaranteedHours - (agent.CurrentHours + agentHours.GetValueOrDefault(agent.Id));
            if (deficit > maxDeficit) { maxDeficit = deficit; hungriest = agent; }
        }

        if (hungriest is null || maxDeficit <= 0) return;

        var assignedIds = new HashSet<string>(assignments.Select(a => a.ShiftId));
        var unassigned = shifts.Where(s => !assignedIds.Contains(s.Id)).ToList();

        if (unassigned.Count > 0)
        {
            var target = unassigned[(int)(rng() * unassigned.Count)];
            assignments.Add(new CoreAssignment(target.Id, hungriest.Id, hungriest.Motivation));
            return;
        }

        CoreAgent? overSupplied = null;
        var maxSurplus = 0.0;
        foreach (var agent in agents)
        {
            if (agent.Id == hungriest.Id) continue;
            var surplus = agent.CurrentHours + agentHours.GetValueOrDefault(agent.Id) - agent.GuaranteedHours;
            if (surplus > maxSurplus) { maxSurplus = surplus; overSupplied = agent; }
        }

        if (overSupplied is not null)
        {
            var targets = assignments
                .Select((a, i) => (a, i))
                .Where(x => x.a.AgentId == overSupplied.Id)
                .ToList();
            if (targets.Count > 0)
            {
                var target = targets[(int)(rng() * targets.Count)];
                assignments[target.i] = assignments[target.i] with { AgentId = hungriest.Id };
            }
        }
    }

    public static (CoreScenario, CoreScenario) CrossoverBlock(
        CoreScenario p1,
        CoreScenario p2,
        List<CoreShift> shifts,
        Func<double> rng)
    {
        var shiftDateMap = new Dictionary<string, string>();
        var uniqueDates = new HashSet<string>();
        foreach (var s in shifts)
        {
            var dk = s.Date.Split('T')[0];
            shiftDateMap[s.Id] = dk;
            uniqueDates.Add(dk);
        }

        var dates = uniqueDates.ToList();
        var swapDates = new HashSet<string>();
        foreach (var d in dates)
        {
            if (rng() < EvolutionConstants.CROSSOVER_SWAP_PROBABILITY) swapDates.Add(d);
        }
        if (swapDates.Count == 0 && dates.Count > 0)
            swapDates.Add(dates[(int)(rng() * dates.Count)]);

        var m1 = p1.Assignments.ToDictionary(a => a.ShiftId);
        var m2 = p2.Assignments.ToDictionary(a => a.ShiftId);

        var c1 = new List<CoreAssignment>();
        var c2 = new List<CoreAssignment>();
        var allIds = new HashSet<string>(m1.Keys.Concat(m2.Keys));

        foreach (var sid in allIds)
        {
            var dk = shiftDateMap.GetValueOrDefault(sid, string.Empty);
            if (swapDates.Contains(dk))
            {
                if (m2.TryGetValue(sid, out var a2)) c1.Add(a2);
                if (m1.TryGetValue(sid, out var a1)) c2.Add(a1);
            }
            else
            {
                if (m1.TryGetValue(sid, out var a1)) c1.Add(a1);
                if (m2.TryGetValue(sid, out var a2)) c2.Add(a2);
            }
        }

        return (
            new CoreScenario { Id = GenerateId(rng), Assignments = c1 },
            new CoreScenario { Id = GenerateId(rng), Assignments = c2 }
        );
    }

    private static CoreScenario TournamentSelect(List<CoreScenario> population, Func<double> rng)
    {
        var best = population[(int)(rng() * population.Count)];
        for (var i = 1; i < SchedulingConstants.TOURNAMENT_SIZE; i++)
        {
            var candidate = population[(int)(rng() * population.Count)];
            if (candidate.Fitness > best.Fitness) best = candidate;
        }
        return best;
    }

    private static CoreScenario CloneScenario(CoreScenario scenario, Func<double> rng)
        => new() { Id = GenerateId(rng), Assignments = new List<CoreAssignment>(scenario.Assignments) };

    private static string GenerateId(Func<double> rng)
        => $"evo_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{(int)(rng() * EvolutionConstants.ID_RANDOM_RANGE)}";

    private static string GetPreviousDayKey(string dateStr)
        => DateTime.Parse(dateStr).AddDays(-1).ToString("yyyy-MM-dd");

    private static int CountHardViolationsFast(
        List<CoreAssignment> assignments,
        List<CoreShift> shifts,
        List<CoreAgent> agents)
    {
        var shiftMap = new Dictionary<string, CoreShift>(shifts.Count);
        foreach (var s in shifts) shiftMap[s.Id] = s;
        var agentMap = new Dictionary<string, CoreAgent>(agents.Count);
        foreach (var a in agents) agentMap[a.Id] = a;

        var agentDailyHours = new Dictionary<string, double>();
        var agentTimeSlots = new Dictionary<string, List<(string Date, string Start, string End)>>();
        var violations = 0;

        foreach (var assignment in assignments)
        {
            if (!shiftMap.TryGetValue(assignment.ShiftId, out var shift)) continue;
            var dateKey = shift.Date.Split('T')[0];
            var dailyKey = $"{assignment.AgentId}_{dateKey}";

            agentDailyHours[dailyKey] = agentDailyHours.GetValueOrDefault(dailyKey) + shift.Hours;

            if (!agentTimeSlots.TryGetValue(assignment.AgentId, out var slots))
            {
                slots = [];
                agentTimeSlots[assignment.AgentId] = slots;
            }
            slots.Add((dateKey, shift.StartTime, shift.EndTime));
        }

        foreach (var (key, hours) in agentDailyHours)
        {
            var agentId = key[..key.LastIndexOf('_')];
            if (agentMap.TryGetValue(agentId, out var agent) && hours > agent.MaxDailyHours)
                violations++;
        }

        foreach (var (agentId, slots) in agentTimeSlots)
        {
            if (!agentMap.TryGetValue(agentId, out var agent)) continue;

            var weeklyHours = new Dictionary<string, double>();
            var dayGroups = new Dictionary<string, List<int>>();
            for (var i = 0; i < slots.Count; i++)
            {
                var dk = slots[i].Date;
                if (!dayGroups.ContainsKey(dk)) dayGroups[dk] = [];
                dayGroups[dk].Add(i);
            }

            foreach (var (dk, indices) in dayGroups)
            {
                for (var i = 0; i < indices.Count; i++)
                    for (var j = i + 1; j < indices.Count; j++)
                    {
                        var si = slots[indices[i]];
                        var sj = slots[indices[j]];
                        if (string.Compare(si.Start, sj.End, StringComparison.Ordinal) < 0 &&
                            string.Compare(sj.Start, si.End, StringComparison.Ordinal) < 0)
                            violations++;
                    }
            }

            var sortedDates = dayGroups.Keys.OrderBy(d => d).ToList();
            var consecutive = 1;
            for (var i = 1; i < sortedDates.Count; i++)
            {
                if ((DateTime.Parse(sortedDates[i]) - DateTime.Parse(sortedDates[i - 1])).TotalDays == 1)
                {
                    consecutive++;
                    if (consecutive > agent.MaxConsecutiveDays) violations++;
                }
                else consecutive = 1;
            }
        }

        return violations;
    }

    private static int CountSoftViolationsFast(
        List<CoreAssignment> assignments,
        List<CoreShift> shifts,
        List<CoreAgent> agents)
    {
        var violations = 0;
        foreach (var a in assignments)
        {
            if (a.MotivationScore < SchedulingConstants.LOW_MOTIVATION_THRESHOLD)
                violations++;
        }

        var agentHours = new Dictionary<string, double>();
        var shiftMap = new Dictionary<string, CoreShift>(shifts.Count);
        foreach (var s in shifts) shiftMap[s.Id] = s;

        foreach (var a in assignments)
        {
            if (shiftMap.TryGetValue(a.ShiftId, out var shift))
                agentHours[a.AgentId] = agentHours.GetValueOrDefault(a.AgentId) + shift.Hours;
        }

        foreach (var agent in agents)
        {
            var totalHours = agentHours.GetValueOrDefault(agent.Id) + agent.CurrentHours;
            if (agent.GuaranteedHours > 0 && totalHours > agent.GuaranteedHours * SchedulingConstants.OVERTIME_THRESHOLD_FACTOR)
                violations++;
        }

        if (agentHours.Count > 1)
        {
            var hours = agentHours.Values.ToList();
            var avg = hours.Average();
            if (avg > 0)
            {
                var maxDev = hours.Max(h => Math.Abs(h - avg));
                if (maxDev / avg > SchedulingConstants.FAIRNESS_MAX_DEVIATION_RATIO)
                    violations++;
            }
        }

        return violations;
    }

    private static EvolutionResult BuildResult(
        List<CoreScenario> population,
        int finalGeneration,
        string message,
        string stopReason,
        long timeElapsedMs)
    {
        population.Sort((a, b) => b.Fitness.CompareTo(a.Fitness));
        var best = population[0];
        return new EvolutionResult
        {
            Assignments = best.Assignments,
            Fitness = best.Fitness,
            Coverage = best.Coverage,
            PenaltyScore = best.PenaltyScore,
            HardViolations = best.HardViolations,
            FinalGeneration = finalGeneration,
            StopReason = stopReason,
            Message = message,
            TimeElapsedMs = timeElapsedMs
        };
    }
}
