// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction.Agent;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction.Controller;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Auction.Conductor;

/// <summary>
/// Layer 1 — Dirigent. Iterates slots in chronological order. Per slot, agents are offered the
/// slot in strict index order (= position in context.Agents). The first agent that accepts
/// (fuzzy bid score &gt;= AcceptanceThreshold AND no Stage-0/Stage-1 veto) wins the slot.
/// If nobody accepts in Round 1, Stage-1 vetos are relaxed (Round 2). If nobody is Stage-0-clean,
/// the slot stays unassigned (Round 3) — no force-assign.
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
        foreach (var agent in context.Agents)
        {
            states[agent.Id] = AgentRuntimeState.Initial(agent.Id);
        }

        var escalation = new EscalationLog();
        var auctionResults = new List<AuctionResult>(orderedSlots.Count);

        foreach (var slot in orderedSlots)
        {
            var result = AwardSlot(slot, tokens, states, context, escalation);
            auctionResults.Add(result);
            if (result.WinnerAgentId is null)
            {
                continue;
            }

            var token = BuildToken(slot, result.WinnerAgentId);
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
        EscalationLog escalation)
    {
        var bids = new List<Bid>(context.Agents.Count);
        var vetos = new List<VetoVerdict>(context.Agents.Count);
        var slotId = $"{slot.Date}/{slot.Id}";

        Bid? round2Fallback = null;
        VetoVerdict? round2FallbackV1 = null;

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
                if (round2Fallback is null && bid.Score >= AcceptanceThreshold)
                {
                    round2Fallback = bid;
                    round2FallbackV1 = v1;
                }
                continue;
            }

            if (bid.Score >= AcceptanceThreshold)
            {
                return new AuctionResult(slotId, agent.Id, 1, bids, vetos);
            }
        }

        if (round2Fallback != null)
        {
            if (DateOnly.TryParse(slot.Date, out var date) && round2FallbackV1 != null)
            {
                escalation.Record(round2Fallback.AgentId, date, round2FallbackV1);
            }
            return new AuctionResult(slotId, round2Fallback.AgentId, 2, bids, vetos);
        }

        return new AuctionResult(slotId, null, 3, bids, vetos);
    }

    private static CoreToken BuildToken(CoreShift slot, string agentId)
    {
        var date = DateOnly.Parse(slot.Date);
        var start = TimeOnly.TryParse(slot.StartTime, out var s) ? s : new TimeOnly(8, 0);
        var end = TimeOnly.TryParse(slot.EndTime, out var e) ? e : start.AddHours(8);
        var shiftTypeIndex = ShiftTypeInference.FromStartTime(start);

        return new CoreToken(
            WorkIds: [],
            ShiftTypeIndex: shiftTypeIndex,
            Date: date,
            TotalHours: (decimal)slot.Hours,
            StartAt: date.ToDateTime(start),
            EndAt: end <= start ? date.AddDays(1).ToDateTime(end) : date.ToDateTime(end),
            BlockId: Guid.NewGuid(),
            PositionInBlock: 0,
            IsLocked: false,
            LocationContext: null,
            ShiftRefId: Guid.TryParse(slot.Id, out var sr) ? sr : Guid.Empty,
            AgentId: agentId);
    }

    private static AgentRuntimeState ApplyAssignmentToState(AgentRuntimeState prev, CoreToken token)
    {
        var newBlockLength = prev.LastWorkedDate.HasValue && prev.LastWorkedDate.Value.AddDays(1) == token.Date
            ? prev.CurrentBlockLength + 1
            : 1;

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
        };
    }
}

/// <summary>Outcome of a full auction run including telemetry.</summary>
public sealed record AuctionRunOutcome(
    CoreScenario Scenario,
    IReadOnlyList<AuctionResult> Results,
    EscalationLog Escalation);
