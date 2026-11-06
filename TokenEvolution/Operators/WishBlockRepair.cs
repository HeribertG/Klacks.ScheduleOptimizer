// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using Klacks.ScheduleOptimizer.Constraints.Keywords;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// Deterministic repair of a block of positive keyword wishes that a single exchange (<see cref="WishRepairSwap"/>) cannot
/// fulfil, because the employee's other shifts around the block collide with the package rules (MaxWorkDays, MinRestDays,
/// rest hours). M3 real data: Lena wishes NIGHT 14-18 Dec; five nights need her free on 12-13 and 19-20. One move does
/// three things at once: the wished shifts on the open block days go from their holders to the employee; her own shifts
/// on the block days of an unwanted kind and inside the rest window (MinRestDays days before and after the block) go to
/// the holders first, otherwise to any other employee in roster order.
/// <para>
/// Every receiving side passes <see cref="SlotConstraintFilter.IsValidAssignment"/> without relaxation (eligibility incl.
/// availability over midnight, contract day, breaks, own keyword restrictions, block length, rest days, pause, hours),
/// checked in order against the shifts the receiver holds at that point of the move. A move is accepted only through
/// <see cref="WishRepairSwap.IsRepair"/>: fewer unfulfilled wishes, legality, coverage, broken tiers, Stage 1 and Stage 2
/// not worse; a move that costs anyone the guaranteed hours therefore never wins. Locked shifts and open carried-in
/// packages never move; when one of them sits in the way the block is skipped.
/// </para>
/// <para>
/// Bounded: a block longer than the employee's MaxWorkDays is not tried, at most <see cref="HolderOptions"/> holder
/// choices per block and <see cref="MaxEvaluations"/> evaluations per call. No random source.
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
    /// <param name="scenario">Plan to repair; never modified</param>
    /// <param name="context">Wizard context supplying the roster, the rules and the schedule commands</param>
    /// <param name="evaluator">Fitness evaluator used as the per-move acceptance gate</param>
    public CoreScenario Apply(CoreScenario scenario, CoreWizardContext context, TokenFitnessEvaluator evaluator)
    {
        if (context.Agents.Count < 2 || scenario.Tokens.Count == 0 || context.ScheduleCommands.Count == 0)
        {
            return scenario;
        }

        var wishes = WizardKeywordWishes.For(context);
        if (wishes.Days.Count == 0)
        {
            return scenario;
        }

        evaluator.Evaluate(scenario, context);
        var continuation = HandoverGeometry.BuildContinuationDays(context);
        var evaluationsLeft = new StrongBox<int>(MaxEvaluations);
        var current = scenario;
        for (var move = 0; move < MaxRepairs && evaluationsLeft.Value > 0; move++)
        {
            var accepted = RepairOneBlock(current, context, evaluator, wishes, continuation, evaluationsLeft);
            if (accepted is null)
            {
                break;
            }

            current = accepted;
        }

        return current;
    }

    private static CoreScenario? RepairOneBlock(
        CoreScenario current,
        CoreWizardContext context,
        TokenFitnessEvaluator evaluator,
        WizardKeywordWishes wishes,
        IReadOnlySet<(string AgentId, DateOnly Date, Guid ShiftRefId)> continuation,
        StrongBox<int> evaluationsLeft)
    {
        var open = wishes.Evaluate(current.Tokens, collectItems: true).Items
            .Where(item => item.Outcome == KeywordWishOutcome.Unfulfilled)
            .Select(item => (item.AgentId, item.Date))
            .ToHashSet();
        if (open.Count == 0)
        {
            return null;
        }

        foreach (var agent in context.Agents)
        {
            foreach (var block in WishBlocks(wishes, agent.Id, open))
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

                    var candidateTokens = BuildMove(current.Tokens, context, wishes, agent, block, open, option, continuation);
                    if (candidateTokens is null)
                    {
                        break;
                    }

                    var candidate = TokenSwapMutation.CloneScenario(current, candidateTokens);
                    evaluationsLeft.Value--;
                    evaluator.Evaluate(candidate, context);
                    if (WishRepairSwap.IsRepair(candidate, current))
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
                if (run.Any(date => open.Contains((agentId, date))))
                {
                    yield return run;
                }

                run = [];
            }

            run.Add(day);
        }

        if (run.Count > 0 && run.Any(date => open.Contains((agentId, date))))
        {
            yield return run;
        }
    }

    /// <summary>
    /// The tokens after the block move with holder option <paramref name="option"/>, or null when the option does not
    /// exist (no holder left on an open day) or a receiving side refuses a shift.
    /// </summary>
    private static List<CoreToken>? BuildMove(
        IReadOnlyList<CoreToken> tokens,
        CoreWizardContext context,
        WizardKeywordWishes wishes,
        CoreAgent wisher,
        IReadOnlyList<DateOnly> block,
        IReadOnlySet<(string AgentId, DateOnly Date)> open,
        int option,
        IReadOnlySet<(string AgentId, DateOnly Date, Guid ShiftRefId)> continuation)
    {
        var owners = tokens.Select(token => token.AgentId).ToArray();
        var rosterIndex = context.Agents.Select((agent, index) => (agent.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.Ordinal);

        var taken = new List<int>();
        foreach (var day in block.Where(day => open.Contains((wisher.Id, day))))
        {
            var wanted = wishes.Days[(wisher.Id, day)].WantedKinds;
            var holders = Enumerable.Range(0, tokens.Count)
                .Where(i => tokens[i].Date == day
                    && !string.Equals(tokens[i].AgentId, wisher.Id, StringComparison.Ordinal)
                    && (KeywordShiftKinds.FromShiftTypeIndex(tokens[i].ShiftTypeIndex) & wanted) != KeywordShiftKinds.None
                    && WishRepairSwap.IsMovable(tokens[i], continuation)
                    && rosterIndex.ContainsKey(tokens[i].AgentId))
                .OrderBy(i => rosterIndex[tokens[i].AgentId])
                .ThenBy(i => tokens[i].StartAt)
                .ToList();
            if (option >= holders.Count)
            {
                return null;
            }

            taken.Add(holders[option]);
        }

        var conflicts = ConflictsOf(tokens, wisher, block, wishes, continuation);
        if (conflicts is null)
        {
            return null;
        }

        foreach (var index in conflicts)
        {
            owners[index] = string.Empty;
        }

        foreach (var index in taken)
        {
            if (!WishRepairSwap.AcceptsToken(wisher, tokens[index], Held(tokens, owners, wisher.Id), context))
            {
                return null;
            }

            owners[index] = wisher.Id;
        }

        var receivers = taken.Select(index => tokens[index].AgentId).Distinct(StringComparer.Ordinal)
            .Concat(context.Agents.Select(agent => agent.Id))
            .Where(id => !string.Equals(id, wisher.Id, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Select(id => context.Agents.First(agent => string.Equals(agent.Id, id, StringComparison.Ordinal)))
            .ToList();

        foreach (var index in conflicts)
        {
            var receiver = receivers.FirstOrDefault(agent =>
                WishRepairSwap.AcceptsToken(agent, tokens[index], Held(tokens, owners, agent.Id), context));
            if (receiver is null)
            {
                return null;
            }

            owners[index] = receiver.Id;
        }

        var agents = context.Agents.ToDictionary(agent => agent.Id, StringComparer.Ordinal);
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
        WizardKeywordWishes wishes,
        IReadOnlySet<(string AgentId, DateOnly Date, Guid ShiftRefId)> continuation)
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
                && (KeywordShiftKinds.FromShiftTypeIndex(token.ShiftTypeIndex) & wishes.Days[(wisher.Id, token.Date)].WantedKinds)
                    != KeywordShiftKinds.None)
            {
                continue;
            }

            if (!WishRepairSwap.IsMovable(token, continuation))
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
