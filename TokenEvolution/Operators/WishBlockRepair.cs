// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using Klacks.ScheduleOptimizer.Constraints.Keywords;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// Deterministic repair of a block of positive keyword wishes that a single exchange (<see cref="WishRepairSwap"/>) cannot
/// fulfil, because the employee's other shifts around the block collide with the package rules (MaxWorkDays, MinRestDays,
/// rest hours). Example from real data: an employee wishes NIGHT on five days in a row and needs to be free on the two
/// days before and after. One move does three things at once: the wished shifts on the open block days go from their
/// holders to the employee; her own shifts on the block days of an unwanted kind and inside the rest window (MinRestDays
/// days before and after the block) go to the holders first, otherwise to any other employee in roster order.
/// <para>
/// Every receiving side passes
/// <see cref="Klacks.ScheduleOptimizer.TokenEvolution.Initialization.SlotConstraintFilter.IsValidAssignment"/> without
/// relaxation (eligibility incl. availability over midnight, contract day, breaks, own keyword restrictions, block length,
/// rest days, pause, hours), checked in order against the shifts the receiver holds at that point of the move. A move is
/// accepted only through <see cref="WishRepairSwap.IsAcceptedRepair"/>: fewer unfulfilled wishes; legality, coverage,
/// broken tiers, Stage 1 and Stage 2 not worse; and no employee who reached the guaranteed hours falls short of them.
/// Locked shifts and open carried-in packages never move: when one of the employee's own shifts in the window is one of
/// them the block is skipped, and such a shift of another employee is never chosen as a holder's shift.
/// </para>
/// <para>
/// Bounded: a block longer than the employee's MaxWorkDays is not tried, at most <see cref="HolderOptions"/> holder
/// choices per block and <see cref="MaxEvaluations"/> fitness evaluations per call. No random source.
/// </para>
/// </summary>
public sealed class WishBlockRepair
{
    /// <summary>Upper bound on accepted moves per invocation; each one lowers the unfulfilled count.</summary>
    private const int MaxRepairs = 16;

    /// <summary>Upper bound on full fitness evaluations per invocation.</summary>
    private const int MaxEvaluations = 60;

    /// <summary>Holder choices tried per block: option k takes the k-th candidate holder on every open day.</summary>
    private const int HolderOptions = 4;

    /// <summary>
    /// Returns a plan with fewer unfulfilled positive wishes, or the unchanged input when no block move improves it.
    /// </summary>
    /// <param name="scenario">Plan to repair; its tokens are never modified, its fitness fields are (re)evaluated</param>
    /// <param name="context">Wizard context supplying the roster, the rules and the schedule commands</param>
    /// <param name="evaluator">Fitness evaluator used as the per-move acceptance gate</param>
    public CoreScenario Apply(CoreScenario scenario, CoreWizardContext context, TokenFitnessEvaluator evaluator)
        => Repair(scenario, context, evaluator).Scenario;

    /// <summary>
    /// <see cref="Apply"/> together with the number of block moves it accepted, for the run's trace.
    /// </summary>
    /// <param name="scenario">Plan to repair; its tokens are never modified, its fitness fields are (re)evaluated</param>
    /// <param name="context">Wizard context supplying the roster, the rules and the schedule commands</param>
    /// <param name="evaluator">Fitness evaluator used as the per-move acceptance gate</param>
    public WishRepairOutcome Repair(CoreScenario scenario, CoreWizardContext context, TokenFitnessEvaluator evaluator)
    {
        if (context.Agents.Count < WishRepairSwap.MinAgentsForExchange
            || scenario.Tokens.Count == 0
            || context.ScheduleCommands.Count == 0)
        {
            return new WishRepairOutcome(scenario, 0);
        }

        var wishes = WizardKeywordWishes.For(context);
        if (wishes.Days.Count == 0)
        {
            return new WishRepairOutcome(scenario, 0);
        }

        evaluator.Evaluate(scenario, context);
        var inputs = new WishBlockMoveInputs(
            context,
            wishes,
            HandoverGeometry.BuildContinuationDays(context),
            context.Agents.Select((agent, index) => (agent.Id, index))
                .ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.Ordinal),
            context.Agents.ToDictionary(agent => agent.Id, StringComparer.Ordinal));
        var evaluationsLeft = new StrongBox<int>(MaxEvaluations);
        var current = scenario;
        var acceptedMoves = 0;
        for (var move = 0; move < MaxRepairs && evaluationsLeft.Value > 0; move++)
        {
            var accepted = RepairOneBlock(current, evaluator, inputs, evaluationsLeft);
            if (accepted is null)
            {
                break;
            }

            current = accepted;
            acceptedMoves++;
        }

        return new WishRepairOutcome(current, acceptedMoves);
    }

    private static CoreScenario? RepairOneBlock(
        CoreScenario current,
        TokenFitnessEvaluator evaluator,
        WishBlockMoveInputs inputs,
        StrongBox<int> evaluationsLeft)
    {
        var open = inputs.Wishes.Evaluate(current.Tokens, collectItems: true).Items
            .Where(item => item.Outcome == KeywordWishOutcome.Unfulfilled)
            .Select(item => (item.AgentId, item.Date))
            .ToHashSet();
        if (open.Count == 0)
        {
            return null;
        }

        foreach (var agent in inputs.Context.Agents)
        {
            foreach (var block in WishBlocks(inputs.Wishes, agent.Id, open))
            {
                if (agent.MaxWorkDays > 0 && block.Count > agent.MaxWorkDays)
                {
                    continue;
                }

                for (var option = 0; option < HolderOptions; option++)
                {
                    if (evaluationsLeft.Value <= 0)
                    {
                        return null;
                    }

                    var candidateTokens = BuildMove(current.Tokens, inputs, agent, block, open, option);
                    if (candidateTokens is null)
                    {
                        continue;
                    }

                    var candidate = TokenSwapMutation.CloneScenario(current, candidateTokens);
                    evaluationsLeft.Value--;
                    evaluator.Evaluate(candidate, inputs.Context);
                    if (WishRepairSwap.IsAcceptedRepair(candidate, current, evaluator, inputs.Context))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Maximal runs of consecutive positive-wish days of the agent that contain at least one open wish.</summary>
    private static IEnumerable<IReadOnlyList<DateOnly>> WishBlocks(
        WizardKeywordWishes wishes, string agentId, IReadOnlySet<(string AgentId, DateOnly Date)> open)
    {
        bool HasOpenDay(List<DateOnly> run) => run.Any(date => open.Contains((agentId, date)));

        var days = wishes.Days
            .Where(entry => string.Equals(entry.Key.AgentId, agentId, StringComparison.Ordinal)
                && entry.Value.WantedKinds != KeywordShiftKinds.None)
            .Select(entry => entry.Key.Date)
            .OrderBy(date => date)
            .ToList();

        var run = new List<DateOnly>();
        foreach (var day in days)
        {
            if (run.Count > 0 && run[^1].AddDays(1) != day)
            {
                if (HasOpenDay(run))
                {
                    yield return run;
                }

                run = [];
            }

            run.Add(day);
        }

        if (run.Count > 0 && HasOpenDay(run))
        {
            yield return run;
        }
    }

    /// <summary>
    /// The tokens after the block move with holder option <paramref name="option"/>, or null when the option does not
    /// exist on an open day, a shift in the employee's window may not move or a receiving side refuses a shift.
    /// </summary>
    private static List<CoreToken>? BuildMove(
        IReadOnlyList<CoreToken> tokens,
        WishBlockMoveInputs inputs,
        CoreAgent wisher,
        IReadOnlyList<DateOnly> block,
        IReadOnlySet<(string AgentId, DateOnly Date)> open,
        int option)
    {
        var taken = SelectHolderShifts(tokens, inputs, wisher, block, open, option);
        if (taken is null)
        {
            return null;
        }

        var conflicts = ConflictsOf(tokens, wisher, block, inputs);
        if (conflicts is null)
        {
            return null;
        }

        var owners = tokens.Select(token => token.AgentId).ToArray();
        foreach (var index in conflicts)
        {
            owners[index] = string.Empty;
        }

        foreach (var index in taken)
        {
            if (!WishRepairSwap.AcceptsToken(wisher, tokens[index], Held(tokens, owners, wisher.Id), inputs.Context))
            {
                return null;
            }

            owners[index] = wisher.Id;
        }

        return RehomeConflicts(tokens, owners, inputs, wisher, taken, conflicts)
            ? Materialise(tokens, owners, inputs.Agents)
            : null;
    }

    /// <summary>
    /// On each open block day the <paramref name="option"/>-th movable shift of a wanted kind another employee holds, in
    /// roster and start order; null when some open day has fewer candidates.
    /// </summary>
    private static List<int>? SelectHolderShifts(
        IReadOnlyList<CoreToken> tokens,
        WishBlockMoveInputs inputs,
        CoreAgent wisher,
        IReadOnlyList<DateOnly> block,
        IReadOnlySet<(string AgentId, DateOnly Date)> open,
        int option)
    {
        var taken = new List<int>();
        foreach (var day in block.Where(day => open.Contains((wisher.Id, day))))
        {
            var wanted = inputs.Wishes.Days[(wisher.Id, day)].WantedKinds;
            var holders = Enumerable.Range(0, tokens.Count)
                .Where(i => tokens[i].Date == day
                    && !string.Equals(tokens[i].AgentId, wisher.Id, StringComparison.Ordinal)
                    && (KeywordShiftKinds.FromShiftTypeIndex(tokens[i].ShiftTypeIndex) & wanted) != KeywordShiftKinds.None
                    && WishRepairSwap.IsMovable(tokens[i], inputs.Continuation)
                    && inputs.RosterIndex.ContainsKey(tokens[i].AgentId))
                .OrderBy(i => inputs.RosterIndex[tokens[i].AgentId])
                .ThenBy(i => tokens[i].StartAt)
                .ToList();
            if (option >= holders.Count)
            {
                return null;
            }

            taken.Add(holders[option]);
        }

        return taken;
    }

    /// <summary>
    /// Gives each freed shift of the employee to the first receiver that accepts it: the holders of the taken shifts
    /// first, then every other employee in roster order. False when a freed shift finds no receiver.
    /// </summary>
    private static bool RehomeConflicts(
        IReadOnlyList<CoreToken> tokens,
        string[] owners,
        WishBlockMoveInputs inputs,
        CoreAgent wisher,
        IReadOnlyList<int> taken,
        IReadOnlyList<int> conflicts)
    {
        var receivers = taken.Select(index => tokens[index].AgentId)
            .Concat(inputs.Context.Agents.Select(agent => agent.Id))
            .Where(id => !string.Equals(id, wisher.Id, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Select(id => inputs.Agents[id])
            .ToList();

        foreach (var index in conflicts)
        {
            var receiver = receivers.FirstOrDefault(agent =>
                WishRepairSwap.AcceptsToken(agent, tokens[index], Held(tokens, owners, agent.Id), inputs.Context));
            if (receiver is null)
            {
                return false;
            }

            owners[index] = receiver.Id;
        }

        return true;
    }

    private static List<CoreToken> Materialise(
        IReadOnlyList<CoreToken> tokens, IReadOnlyList<string> owners, IReadOnlyDictionary<string, CoreAgent> agents)
    {
        var result = new List<CoreToken>(tokens.Count);
        for (var i = 0; i < tokens.Count; i++)
        {
            result.Add(string.Equals(owners[i], tokens[i].AgentId, StringComparison.Ordinal)
                ? tokens[i]
                : WishRepairSwap.Reowned(tokens[i], agents[owners[i]]));
        }

        return result;
    }

    /// <summary>
    /// The wisher's shifts the block needs freed, in date order: on a block day a shift of an unwanted kind, and every
    /// shift in the rest window of MinRestDays days before and after the block. Null when one of them may not move.
    /// </summary>
    private static List<int>? ConflictsOf(
        IReadOnlyList<CoreToken> tokens,
        CoreAgent wisher,
        IReadOnlyList<DateOnly> block,
        WishBlockMoveInputs inputs)
    {
        var restDays = Math.Max(wisher.MinRestDays, 0);
        var windowStart = block[0].AddDays(-restDays);
        var windowEnd = block[^1].AddDays(restDays);
        var conflicts = new List<int>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (!string.Equals(token.AgentId, wisher.Id, StringComparison.Ordinal)
                || token.Date < windowStart
                || token.Date > windowEnd)
            {
                continue;
            }

            var onBlockDay = token.Date >= block[0] && token.Date <= block[^1];
            if (onBlockDay
                && (KeywordShiftKinds.FromShiftTypeIndex(token.ShiftTypeIndex) & inputs.Wishes.Days[(wisher.Id, token.Date)].WantedKinds)
                    != KeywordShiftKinds.None)
            {
                continue;
            }

            if (!WishRepairSwap.IsMovable(token, inputs.Continuation))
            {
                return null;
            }

            conflicts.Add(i);
        }

        return conflicts.OrderBy(i => tokens[i].Date).ThenBy(i => tokens[i].StartAt).ToList();
    }

    private static List<CoreToken> Held(IReadOnlyList<CoreToken> tokens, IReadOnlyList<string> owners, string agentId)
    {
        var held = new List<CoreToken>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (string.Equals(owners[i], agentId, StringComparison.Ordinal))
            {
                held.Add(tokens[i]);
            }
        }

        return held;
    }
}
