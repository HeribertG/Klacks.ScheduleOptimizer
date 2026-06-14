// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction.Agent;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction.Controller;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Auction.Conductor;

/// <summary>
/// Layer 1 — Dirigent. Iterates slots in chronological order. Per slot, every agent submits a bid;
/// the highest-scoring Stage-0+Stage-1-clean bid above AcceptanceThreshold wins Round 1.
/// If no bid is Stage-1-clean, the highest-scoring Stage-0-clean bid wins Round 2 (escalation
/// logged). If no agent is Stage-0-clean, the slot stays unassigned (Round 3) — no force-assign.
/// Top-down roster rule: while candidates are still below their guaranteed hours, the slot goes
/// to the best-scoring of them (ties broken by roster position, top wins). Once every candidate
/// has reached its target the slot is surplus and goes to the bottom of the roster instead, so
/// the top of the roster stays accurate ("the bottom eats what is left").
/// </summary>
public sealed class SlotAuctioneer
{
    /// <summary>Minimum fuzzy bid score required for an agent to accept a slot. Default 0.3 ≈ Low/Medium.</summary>
    public double AcceptanceThreshold { get; init; } = 0.3;

    private readonly IBiddingAgent _biddingAgent;
    private readonly Stage0HardConstraintChecker _stage0;
    private readonly Stage1SoftConstraintChecker _stage1;

    public SlotAuctioneer(
        IBiddingAgent biddingAgent,
        Stage0HardConstraintChecker stage0,
        Stage1SoftConstraintChecker stage1)
    {
        _biddingAgent = biddingAgent;
        _stage0 = stage0;
        _stage1 = stage1;
    }

    public AuctionRunOutcome Run(CoreWizardContext context, Random rng)
    {
        var tokens = new List<CoreToken>(
            LockedTokenFactory.BuildLockedTokens(context.LockedWorks, context.SchedulingMaxConsecutiveDays));

        var orderedSlots = context.Shifts
            .OrderBy(s => s.Date, StringComparer.Ordinal)
            .ThenBy(s => s.StartTime, StringComparer.Ordinal)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();

        var states = new Dictionary<string, AgentRuntimeState>(StringComparer.Ordinal);
        var rosterPosition = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var agent in context.Agents)
        {
            rosterPosition[agent.Id] = rosterPosition.Count;
            states[agent.Id] = AgentRuntimeState.InitialFromBoundary(
                agent.Id,
                context.PeriodFrom,
                context.BoundaryLockedWorks,
                context.BoundaryExistingWorkBlockers);
        }

        var escalation = new EscalationLog();
        var auctionResults = new List<AuctionResult>(orderedSlots.Count);

        foreach (var slot in orderedSlots)
        {
            var result = AwardSlot(slot, tokens, states, context, escalation, rosterPosition);
            auctionResults.Add(result);
            if (result.WinnerAgentId is null)
            {
                continue;
            }

            var winnerAgent = context.Agents.FirstOrDefault(a => a.Id == result.WinnerAgentId);
            var token = BuildToken(slot, result.WinnerAgentId, winnerAgent);
            tokens.Add(token);

            if (states.TryGetValue(result.WinnerAgentId, out var prev))
            {
                states[result.WinnerAgentId] = ApplyAssignmentToState(prev, token);
            }
        }

        var scenario = new CoreScenario
        {
            Id = Guid.NewGuid().ToString(),
            Tokens = tokens,
        };
        return new AuctionRunOutcome(scenario, auctionResults, escalation);
    }

    private AuctionResult AwardSlot(
        CoreShift slot,
        IReadOnlyList<CoreToken> tokensSoFar,
        IReadOnlyDictionary<string, AgentRuntimeState> states,
        CoreWizardContext context,
        EscalationLog escalation,
        IReadOnlyDictionary<string, int> rosterPosition)
    {
        var bids = new List<Bid>(context.Agents.Count);
        var vetos = new List<VetoVerdict>(context.Agents.Count);
        var slotId = $"{slot.Date}/{slot.Id}";

        var round1Candidates = new List<Bid>();
        var round2Candidates = new List<(Bid Bid, VetoVerdict V1)>();

        foreach (var agent in context.Agents)
        {
            var v0 = _stage0.Check(agent, slot, tokensSoFar, context);
            if (v0 != null)
            {
                vetos.Add(v0);
                continue;
            }

            var bid = _biddingAgent.Evaluate(agent, slot, states[agent.Id], context);
            bids.Add(bid);

            var v1 = _stage1.Check(agent, slot, tokensSoFar, context);
            if (v1 != null)
            {
                vetos.Add(v1);
                if (bid.Score >= AcceptanceThreshold)
                {
                    round2Candidates.Add((bid, v1));
                }
                continue;
            }

            if (bid.Score >= AcceptanceThreshold)
            {
                round1Candidates.Add(bid);
            }
        }

        if (round1Candidates.Count > 0)
        {
            var winner = SelectWinner(round1Candidates, b => b.AgentId, b => b.Score, states, context, rosterPosition);
            return new AuctionResult(slotId, winner.AgentId, 1, bids, vetos);
        }

        if (round2Candidates.Count > 0)
        {
            var winner = SelectWinner(round2Candidates, c => c.Bid.AgentId, c => c.Bid.Score, states, context, rosterPosition);
            if (DateOnly.TryParse(slot.Date, out var date))
            {
                escalation.Record(winner.Bid.AgentId, date, winner.V1);
            }
            return new AuctionResult(slotId, winner.Bid.AgentId, 2, bids, vetos);
        }

        return new AuctionResult(slotId, null, 3, bids, vetos);
    }

    /// <summary>
    /// Picks the winning candidate for a slot according to the top-down roster rule: candidates
    /// still below their guaranteed hours win by score (roster position breaks ties, top first);
    /// when every candidate is already at or above target the slot is surplus and goes to the
    /// bottom-most roster position so the top of the roster keeps its accurate target hours.
    /// </summary>
    private static T SelectWinner<T>(
        IReadOnlyList<T> candidates,
        Func<T, string> agentIdOf,
        Func<T, double> scoreOf,
        IReadOnlyDictionary<string, AgentRuntimeState> states,
        CoreWizardContext context,
        IReadOnlyDictionary<string, int> rosterPosition)
    {
        var agentLookup = context.Agents.ToDictionary(a => a.Id, StringComparer.Ordinal);

        bool IsBelowTarget(string agentId)
        {
            if (!agentLookup.TryGetValue(agentId, out var agent) || agent.GuaranteedHours <= 0)
            {
                return false;
            }
            var assigned = states.TryGetValue(agentId, out var state) ? state.HoursAssignedThisRun : 0;
            return agent.CurrentHours + assigned < agent.GuaranteedHours;
        }

        var belowTarget = candidates.Where(c => IsBelowTarget(agentIdOf(c))).ToList();
        if (belowTarget.Count > 0)
        {
            return belowTarget
                .OrderByDescending(scoreOf)
                .ThenBy(c => rosterPosition.GetValueOrDefault(agentIdOf(c), int.MaxValue))
                .ThenBy(c => agentIdOf(c), StringComparer.Ordinal)
                .First();
        }

        return candidates
            .OrderByDescending(c => rosterPosition.GetValueOrDefault(agentIdOf(c), int.MinValue))
            .ThenByDescending(scoreOf)
            .ThenBy(c => agentIdOf(c), StringComparer.Ordinal)
            .First();
    }

    private static CoreToken BuildToken(CoreShift slot, string agentId, CoreAgent? agent)
    {
        var date = DateOnly.Parse(slot.Date);
        var start = TimeOnly.TryParse(slot.StartTime, out var s) ? s : new TimeOnly(8, 0);
        var end = TimeOnly.TryParse(slot.EndTime, out var e) ? e : start.AddHours(8);
        var shiftTypeIndex = ShiftTypeInference.FromStartTime(start);
        var totalHours = (decimal)slot.Hours;

        return new CoreToken(
            WorkIds: [],
            ShiftTypeIndex: shiftTypeIndex,
            Date: date,
            TotalHours: totalHours,
            StartAt: date.ToDateTime(start),
            EndAt: end <= start ? date.AddDays(1).ToDateTime(end) : date.ToDateTime(end),
            BlockId: Guid.NewGuid(),
            PositionInBlock: 0,
            IsLocked: false,
            LocationContext: null,
            ShiftRefId: Guid.TryParse(slot.Id, out var sr) ? sr : Guid.Empty,
            AgentId: agentId)
        {
            Surcharges = EstimateSurcharges(totalHours, shiftTypeIndex, date, agent),
        };
    }

    /// <summary>
    /// Rough surcharge estimate for the wizard planning fitness: applies the agent's contract rates
    /// (Night/Sa/So) by shift type and weekday. Rates are stored as multipliers (0.10 = 10%), so the
    /// estimate is simply hours x rate. Holiday rate is intentionally skipped because the wizard
    /// does not load calendar selections — the actual surcharge will be computed precisely by
    /// WorkMacroService at apply time.
    /// </summary>
    private static decimal EstimateSurcharges(decimal totalHours, int shiftTypeIndex, DateOnly date, CoreAgent? agent)
    {
        if (agent is null || totalHours <= 0)
        {
            return 0m;
        }
        var rate = 0m;
        if (shiftTypeIndex == 2) rate += agent.NightRate;
        if (date.DayOfWeek == DayOfWeek.Saturday) rate += agent.SaRate;
        if (date.DayOfWeek == DayOfWeek.Sunday) rate += agent.SoRate;
        if (rate <= 0) return 0m;
        return totalHours * rate;
    }

    private static AgentRuntimeState ApplyAssignmentToState(AgentRuntimeState prev, CoreToken token)
    {
        var sameDay = prev.LastWorkedDate.HasValue && prev.LastWorkedDate.Value == token.Date;
        var continuesBlock = sameDay
            || (prev.LastWorkedDate.HasValue && prev.LastWorkedDate.Value.AddDays(1) == token.Date);
        var newBlockLength = sameDay
            ? Math.Max(prev.CurrentBlockLength, 1)
            : continuesBlock ? prev.CurrentBlockLength + 1 : 1;

        var daysSince = prev.DaysSinceShiftType.ToArray();
        for (var i = 0; i < daysSince.Length; i++)
        {
            daysSince[i] = i == token.ShiftTypeIndex ? 0 : daysSince[i] == int.MaxValue ? int.MaxValue : daysSince[i] + 1;
        }

        return prev with
        {
            HoursAssignedThisRun = prev.HoursAssignedThisRun + (double)token.TotalHours,
            CurrentBlockLength = newBlockLength,
            LastWorkedDate = token.Date,
            DaysSinceShiftType = daysSince,
            CurrentBlockStartShiftType = continuesBlock && prev.CurrentBlockStartShiftType >= 0
                ? prev.CurrentBlockStartShiftType
                : token.ShiftTypeIndex,
        };
    }
}

/// <summary>Outcome of a full auction run including telemetry.</summary>
public sealed record AuctionRunOutcome(
    CoreScenario Scenario,
    IReadOnlyList<AuctionResult> Results,
    EscalationLog Escalation);
