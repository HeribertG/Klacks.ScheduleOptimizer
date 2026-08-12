// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using System.Globalization;
using Klacks.ScheduleOptimizer.Constraints;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Constraints;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// Repair operator: resolves a random hard-constraint violation.
/// For token-bound violations (MaxDailyHours, keyword, etc.) the operator removes an offending
/// non-locked token. For slot-bound <see cref="ViolationKind.UnderSupply"/> violations it tries
/// to ADD a valid token filling the missing slot. Locked tokens are never mutated.
/// Index-aware (top-down roster rule): UnderSupply prefers agents still BELOW their guaranteed
/// hours, top roster position first — but once every candidate has reached its target, the
/// surplus slot goes bottom-first so the top of the roster stays accurate ("the bottom eats
/// what is left"). OverSupply and RemoveOffendingToken pick the doomed token with bottom-bias.
/// Together with <see cref="ReassignMutation"/> this gives ~35% of GA mutation calls a
/// top-down distribution pull.
/// <para>
/// Staffing an under-supplied slot climbs the escalation ladder <see cref="SlotRelaxation.None"/> →
/// <see cref="SlotRelaxation.RestDaysOnly"/> → <see cref="SlotRelaxation.All"/> and stops at the
/// first rung that answers. Each rung admits everything the rung before it admits, so the ladder can
/// never cost a fill that the strict rung already had, while the block-ideal-abiding candidate keeps
/// precedence: the widest rung is consulted only where the slot would otherwise stay empty, which is
/// the priority order of the specification (coverage first, the 5/2 package ideal far below it).
/// Since the owner ruling of 2026-08-12 the ladder can no longer trade the package rest: MinRestDays
/// (as hours) vetoes on every rung, so only the MaxWorkDays block ideal remains escalatable.
/// </para>
/// </summary>
public sealed class TokenRepair : ITokenOperator
{
    private const string DirectFill = "direct";

    private const string RelocatedFill = "relocate";

    private const string EscalationDateFormat = "yyyy-MM-dd";

    private static readonly SlotRelaxation[] CoverageLadder =
    [
        SlotRelaxation.None,
        SlotRelaxation.RestDaysOnly,
        SlotRelaxation.All,
    ];

    private readonly TokenConstraintChecker _checker;

    public TokenRepair(TokenConstraintChecker checker)
    {
        _checker = checker;
    }

    public CoreScenario Apply(TokenOperatorContext context) => Apply(context, null);

    /// <summary>
    /// Applies the operator and reports every fill that only the widest rung of the escalation made
    /// possible, so a diagnostics run can tell "the ladder was consulted" from "the ladder fired".
    /// </summary>
    /// <param name="context">Scenario to repair, wizard context and the random source</param>
    /// <param name="escalations">
    /// Optional sink invoked once per slot that was staffed on <see cref="SlotRelaxation.All"/>;
    /// null switches the reporting off and costs one null check per escalated fill.
    /// </param>
    public CoreScenario Apply(TokenOperatorContext context, Action<string>? escalations)
    {
        var violations = _checker.Check(context.Primary, context.Wizard);
        if (violations.Count == 0)
        {
            return TokenSwapMutation.CloneScenario(context.Primary, context.Primary.Tokens.ToList());
        }

        var overSupply = violations.Where(v => v.Kind == ViolationKind.OverSupply).ToList();
        if (overSupply.Count > 0)
        {
            var pick = overSupply[context.Rng.Next(overSupply.Count)];
            return RepairOverSupply(context, pick);
        }

        var underSupply = violations.Where(v => v.Kind == ViolationKind.UnderSupply).ToList();
        if (underSupply.Count > 0)
        {
            var pick = underSupply[context.Rng.Next(underSupply.Count)];
            return TryRepairUnderSupply(
                context.Primary, context.Wizard, context.Rng, pick, escalations, out var repaired)
                ? repaired
                : TokenSwapMutation.CloneScenario(context.Primary, context.Primary.Tokens.ToList());
        }

        var violation = violations[context.Rng.Next(violations.Count)];
        return RemoveOffendingToken(context, violation);
    }

    /// <summary>
    /// Deterministic sweep used by the GA loop: iterates every distinct UnderSupply violation and
    /// attempts to fill the corresponding slot with a valid agent. Skips slots without any valid
    /// candidate (theoretically unfillable) and moves on, so a single unreachable slot cannot abort
    /// coverage recovery for the remaining fillable ones.
    /// </summary>
    /// <param name="scenario">Plan to complete; never modified.</param>
    /// <param name="context">Wizard context supplying agents, slots and rules.</param>
    /// <param name="rng">Random source of the sweep.</param>
    /// <param name="cancellationToken">Cancels the sweep between slots.</param>
    /// <param name="trace">Optional textual trace of the sweep's iterations.</param>
    /// <param name="escalations">
    /// Optional sink invoked once per slot that only the widest rung of the escalation could staff.
    /// </param>
    public CoreScenario FillAllUnderSupply(
        CoreScenario scenario,
        CoreWizardContext context,
        Random rng,
        CancellationToken cancellationToken = default,
        Action<string>? trace = null,
        Action<string>? escalations = null)
    {
        var current = scenario;
        var iter = 0;

        // The sweep only ADDS tokens, so a slot that could not be filled cannot become fillable later in
        // the same call: the occupied agents grow monotonically and the capacity check never frees up.
        // The set is per call - across generations fillability depends on the genome and must be retried.
        var failedSlots = new HashSet<(Guid ShiftRefId, DateOnly Date)>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            iter++;
            var violations = _checker.Check(current, context);
            cancellationToken.ThrowIfCancellationRequested();

            var pending = violations
                .Where(v => v.Kind == ViolationKind.UnderSupply && v.ShiftRefId.HasValue && v.Date.HasValue)
                .Select(v => (Key: (v.ShiftRefId!.Value, v.Date!.Value), Violation: v))
                .GroupBy(x => x.Key)
                .Select(g => g.First().Violation)
                .ToList();

            if (pending.Count == 0)
            {
                trace?.Invoke($"FillAllUnderSupply: done iter={iter} tokens={current.Tokens.Count} (no pending)");
                return current;
            }

            if (iter > 1 && iter % 5 == 0)
            {
                trace?.Invoke($"FillAllUnderSupply: iter={iter} pending={pending.Count} tokens={current.Tokens.Count}");
            }

            var progress = false;
            foreach (var violation in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var slotKey = (violation.ShiftRefId!.Value, violation.Date!.Value);
                if (failedSlots.Contains(slotKey))
                {
                    continue;
                }

                if (TryRepairUnderSupply(current, context, rng, violation, escalations, out var repaired))
                {
                    current = repaired;
                    progress = true;
                }
                else
                {
                    failedSlots.Add(slotKey);
                }
            }

            if (!progress)
            {
                trace?.Invoke($"FillAllUnderSupply: done iter={iter} tokens={current.Tokens.Count} (no progress, {pending.Count} unfillable)");
                return current;
            }
        }
    }

    private static CoreScenario RemoveOffendingToken(TokenOperatorContext context, ConstraintViolation violation)
    {
        var candidates = context.Primary.Tokens
            .Where(t => !t.IsLocked && MatchesViolation(t, violation))
            .ToList();

        if (candidates.Count == 0)
        {
            return TokenSwapMutation.CloneScenario(context.Primary, context.Primary.Tokens.ToList());
        }

        var doomed = RosterPositionBias.PickWithBottomBias(candidates, t => t.AgentId, context.Wizard.Agents, context.Rng);
        var remaining = context.Primary.Tokens.Where(t => t != doomed).ToList();
        return TokenSwapMutation.CloneScenario(context.Primary, remaining);
    }

    /// <summary>
    /// Tries to staff one under-supplied slot. Returns false without cloning anything when the slot
    /// cannot be filled - the sweep runs this for every pending violation of every generation, and
    /// cloning the whole genome just to throw it away again was the dominant allocation of a repair pass.
    /// </summary>
    /// <param name="primary">Scenario to repair; never modified.</param>
    /// <param name="wizard">Wizard context supplying agents, slots and rules.</param>
    /// <param name="rng">Random source of the operator.</param>
    /// <param name="violation">The under-supply violation to answer.</param>
    /// <param name="escalations">Optional sink for fills that only the widest rung made possible.</param>
    /// <param name="repaired">The repaired scenario, valid only when the method returns true.</param>
    private static bool TryRepairUnderSupply(
        CoreScenario primary,
        CoreWizardContext wizard,
        Random rng,
        ConstraintViolation violation,
        Action<string>? escalations,
        out CoreScenario repaired)
    {
        repaired = primary;

        if (!violation.ShiftRefId.HasValue || !violation.Date.HasValue)
        {
            return false;
        }

        var slot = FindSlot(wizard, violation.ShiftRefId.Value, violation.Date.Value);
        if (slot is null)
        {
            return false;
        }

        var capacity = Math.Max(1, slot.RequiredAssignments);
        var assigned = 0;
        foreach (var token in primary.Tokens)
        {
            if (token.ShiftRefId == violation.ShiftRefId.Value && token.Date == violation.Date.Value)
            {
                assigned++;
            }
        }

        if (assigned >= capacity)
        {
            return false;
        }

        var start = TimeOnly.TryParse(slot.StartTime, out var parsedStart) ? parsedStart : new TimeOnly(8, 0);
        var end = TimeOnly.TryParse(slot.EndTime, out var parsedEnd) ? parsedEnd : start.AddHours(8);
        var shiftTypeIndex = ShiftTypeInference.FromSpan(start, end);
        var slotHours = (decimal)slot.Hours;
        var slotStartUtc = violation.Date.Value.ToDateTime(start);
        var slotEndUtc = end <= start ? violation.Date.Value.AddDays(1).ToDateTime(end) : violation.Date.Value.ToDateTime(end);

        var occupiedAgents = primary.Tokens
            .Where(t => t.ShiftRefId == violation.ShiftRefId.Value && t.Date == violation.Date.Value)
            .Select(t => t.AgentId)
            .ToHashSet(StringComparer.Ordinal);

        // The escalation ladder. Every rung first offers the slot to the agents that can take it
        // directly and then to the one-move relocation, and the walk stops at the first rung with an
        // answer. Rung 1 keeps every rule. Rung 2 is a historic no-op since the owner ruling of
        // 2026-08-12: the rest between two packages (MinRestDays as hours) may no longer step aside
        // for coverage. Rung 3 lets the MaxWorkDays block ideal step aside, which is what a slot
        // needing the sixth consecutive day costs; it runs only where the slot would otherwise stay
        // empty, so the block-ideal-abiding candidate always wins where one exists. No rung relaxes a
        // hard rule - bans, rest hours and days, the consecutive-days cap and collisions gate the
        // candidate on all three.
        List<CoreAgent> candidates = [];
        var reached = SlotRelaxation.None;

        foreach (var rung in CoverageLadder)
        {
            candidates = wizard.Agents
                .Where(agent => !occupiedAgents.Contains(agent.Id)
                    && SlotConstraintFilter.IsValidAssignment(
                        agent, violation.Date.Value, shiftTypeIndex, violation.ShiftRefId.Value, slotHours,
                        wizard, primary.Tokens, slotStartUtc, slotEndUtc, rung))
                .ToList();

            if (candidates.Count > 0)
            {
                reached = rung;
                break;
            }

            if (TryRelocateAndFill(
                    primary, wizard, violation.ShiftRefId.Value, violation.Date.Value,
                    shiftTypeIndex, slotHours, slotStartUtc, slotEndUtc, occupiedAgents,
                    rung, escalations, out repaired))
            {
                return true;
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        var chosen = RosterPositionBias.PickAccuracyAware(candidates, primary.Tokens, wizard.Agents, rng);
        if (reached == SlotRelaxation.All)
        {
            ReportEscalation(escalations, chosen.Id, violation.Date.Value, DirectFill);
        }

        var tokens = primary.Tokens.ToList();
        tokens.Add(new CoreToken(
            WorkIds: [],
            ShiftTypeIndex: shiftTypeIndex,
            Date: violation.Date.Value,
            TotalHours: slotHours,
            StartAt: slotStartUtc,
            EndAt: slotEndUtc,
            BlockId: Guid.NewGuid(),
            PositionInBlock: 0,
            IsLocked: false,
            LocationContext: EvaluationContext.For(wizard)
                .LocationContextByShift.GetValueOrDefault(violation.ShiftRefId.Value),
            ShiftRefId: violation.ShiftRefId.Value,
            AgentId: chosen.Id)
        {
            Surcharges = SurchargeEstimator.Estimate(slotHours, shiftTypeIndex, violation.Date.Value, chosen),
        });

        repaired = TokenSwapMutation.CloneScenario(primary, tokens);
        return true;
    }

    /// <summary>
    /// One-move escape for a slot no agent can take directly: an agent that fails only because of one
    /// of its own neighbouring shifts hands that shift to another agent and then takes the open slot.
    /// Deterministic and random-free — agents in roster order, blocker candidates are the outer edge
    /// days of the runs adjacent to the slot (removing an edge never splits a block), receivers are
    /// picked accuracy-aware (below guaranteed hours top-down first, surplus bottom-up second). The
    /// reach is deliberately one move; a blocker in the middle of a run stays where it is. The rung of
    /// the escalation is handed through, so the same escape serves all three of them.
    /// </summary>
    private static bool TryRelocateAndFill(
        CoreScenario primary,
        CoreWizardContext wizard,
        Guid shiftRefId,
        DateOnly slotDate,
        int shiftTypeIndex,
        decimal slotHours,
        DateTime slotStartUtc,
        DateTime slotEndUtc,
        IReadOnlySet<string> occupiedAgents,
        SlotRelaxation relaxation,
        Action<string>? escalations,
        out CoreScenario repaired)
    {
        repaired = primary;

        foreach (var agent in wizard.Agents)
        {
            if (occupiedAgents.Contains(agent.Id))
            {
                continue;
            }

            var ownDays = new HashSet<DateOnly>();
            foreach (var token in primary.Tokens)
            {
                if (string.Equals(token.AgentId, agent.Id, StringComparison.Ordinal))
                {
                    ownDays.Add(token.Date);
                }
            }

            var blockers = EdgeBlockers(primary.Tokens, agent.Id, ownDays, slotDate, agent.MinRestDays);

            foreach (var blocker in blockers)
            {
                if (TryMoveBlockersAndFill(
                        primary, wizard, agent, [blocker], slotDate, shiftTypeIndex, shiftRefId,
                        slotHours, slotStartUtc, slotEndUtc, relaxation, out repaired))
                {
                    ReportRelocationEscalation(escalations, relaxation, agent.Id, slotDate);
                    return true;
                }
            }

            if (relaxation == SlotRelaxation.None)
            {
                continue;
            }

            // Depth-2 cascade, last escalation only: a slot can be walled in by TWO own shifts at
            // once (a collision on the slot day plus a rest-hour neighbour), which no single move
            // resolves. Both blockers hand over, then the slot is taken.
            for (var i = 0; i < blockers.Count; i++)
            {
                for (var j = i + 1; j < blockers.Count; j++)
                {
                    if (TryMoveBlockersAndFill(
                            primary, wizard, agent, [blockers[i], blockers[j]], slotDate, shiftTypeIndex,
                            shiftRefId, slotHours, slotStartUtc, slotEndUtc, relaxation, out repaired))
                    {
                        ReportRelocationEscalation(escalations, relaxation, agent.Id, slotDate);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Removes the given blockers of one agent, verifies the agent may then take the slot, finds a
    /// receiver for every removed blocker on the growing swapped state and builds the repaired plan —
    /// or returns false without side effects when any step has no legal answer. The escalation rung
    /// buys the OPEN slot only: a receiver of a relocated shift is never taken past its own block
    /// ideal, so one escalated move can grow at most one package beyond the ideal.
    /// </summary>
    private static bool TryMoveBlockersAndFill(
        CoreScenario primary,
        CoreWizardContext wizard,
        CoreAgent agent,
        IReadOnlyList<CoreToken> blockers,
        DateOnly slotDate,
        int shiftTypeIndex,
        Guid shiftRefId,
        decimal slotHours,
        DateTime slotStartUtc,
        DateTime slotEndUtc,
        SlotRelaxation relaxation,
        out CoreScenario repaired)
    {
        repaired = primary;
        var removed = new HashSet<CoreToken>(blockers);
        var working = primary.Tokens.Where(t => !removed.Contains(t)).ToList();

        if (!SlotConstraintFilter.IsValidAssignment(
                agent, slotDate, shiftTypeIndex, shiftRefId, slotHours,
                wizard, working, slotStartUtc, slotEndUtc, relaxation))
        {
            return false;
        }

        var receiverRelaxation = ReceiverRelaxation(relaxation);
        foreach (var blocker in blockers)
        {
            var receiver = FindRelocationReceiver(agent, blocker, working, wizard, receiverRelaxation);
            if (receiver is null)
            {
                return false;
            }

            working.Add(blocker with
            {
                AgentId = receiver.Id,
                Surcharges = SurchargeEstimator.Estimate(
                    blocker.TotalHours, blocker.ShiftTypeIndex, blocker.Date, receiver),
            });
        }

        working.Add(new CoreToken(
            WorkIds: [],
            ShiftTypeIndex: shiftTypeIndex,
            Date: slotDate,
            TotalHours: slotHours,
            StartAt: slotStartUtc,
            EndAt: slotEndUtc,
            BlockId: Guid.NewGuid(),
            PositionInBlock: 0,
            IsLocked: false,
            LocationContext: EvaluationContext.For(wizard)
                .LocationContextByShift.GetValueOrDefault(shiftRefId),
            ShiftRefId: shiftRefId,
            AgentId: agent.Id)
        {
            Surcharges = SurchargeEstimator.Estimate(slotHours, shiftTypeIndex, slotDate, agent),
        });

        repaired = TokenSwapMutation.CloneScenario(primary, working);
        return true;
    }

    /// <summary>
    /// The agent's own removable tokens whose removal can free the slot day: any own shift on the
    /// slot day itself (a collision or rest-hour conflict with the slot; the fill keeps the day
    /// occupied, so the run never splits), the outer edge days of the runs directly adjacent to the
    /// slot, and the slot-facing edge days of runs within the MinRestDays window — those are the
    /// shifts whose rest-day gap vetoes the slot even across a free day. Removing an edge never
    /// splits a block. Ordered by date then start for a stable walk.
    /// </summary>
    private static List<CoreToken> EdgeBlockers(
        IReadOnlyList<CoreToken> tokens, string agentId, HashSet<DateOnly> ownDays, DateOnly slotDate, int minRestDays)
    {
        var edgeDays = new HashSet<DateOnly> { slotDate };
        var reach = Math.Max(1, minRestDays);

        for (var offset = 1; offset <= reach; offset++)
        {
            var before = slotDate.AddDays(-offset);
            if (ownDays.Contains(before))
            {
                var probe = before;
                while (ownDays.Contains(probe.AddDays(-1)))
                {
                    probe = probe.AddDays(-1);
                }

                edgeDays.Add(probe);
                edgeDays.Add(before);
                break;
            }
        }

        for (var offset = 1; offset <= reach; offset++)
        {
            var after = slotDate.AddDays(offset);
            if (ownDays.Contains(after))
            {
                var probe = after;
                while (ownDays.Contains(probe.AddDays(1)))
                {
                    probe = probe.AddDays(1);
                }

                edgeDays.Add(probe);
                edgeDays.Add(after);
                break;
            }
        }

        var blockers = new List<CoreToken>();
        foreach (var token in tokens)
        {
            if (!token.IsLocked
                && string.Equals(token.AgentId, agentId, StringComparison.Ordinal)
                && edgeDays.Contains(token.Date)
                && (token.Date == slotDate || IsRunEdge(ownDays, token.Date)))
            {
                blockers.Add(token);
            }
        }

        blockers.Sort((a, b) => a.Date != b.Date
            ? a.Date.CompareTo(b.Date)
            : a.StartAt.CompareTo(b.StartAt));
        return blockers;
    }

    private static bool IsRunEdge(HashSet<DateOnly> ownDays, DateOnly date)
        => !ownDays.Contains(date.AddDays(-1)) || !ownDays.Contains(date.AddDays(1));

    /// <summary>
    /// Deterministic receiver for a relocated shift: agents below their guaranteed hours in roster
    /// order first (top-down service), then the rest in reverse roster order (surplus flows to the
    /// bottom), each gated by the full assignment filter on the given plan state — the state grows
    /// while a multi-blocker cascade re-owns one shift after the other.
    /// </summary>
    private static CoreAgent? FindRelocationReceiver(
        CoreAgent donor,
        CoreToken blocker,
        IReadOnlyList<CoreToken> stateTokens,
        CoreWizardContext wizard,
        SlotRelaxation relaxation)
    {
        var hoursByAgent = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var token in stateTokens)
        {
            hoursByAgent[token.AgentId] = hoursByAgent.GetValueOrDefault(token.AgentId, 0)
                + (double)(token.TotalHours + token.Surcharges);
        }

        var below = new List<CoreAgent>();
        var surplus = new List<CoreAgent>();
        foreach (var candidate in wizard.Agents)
        {
            if (string.Equals(candidate.Id, donor.Id, StringComparison.Ordinal))
            {
                continue;
            }

            var planned = candidate.CurrentHours + hoursByAgent.GetValueOrDefault(candidate.Id, 0);
            if (candidate.GuaranteedHours > 0 && planned < candidate.GuaranteedHours)
            {
                below.Add(candidate);
            }
            else
            {
                surplus.Add(candidate);
            }
        }

        surplus.Reverse();

        foreach (var candidate in below.Concat(surplus))
        {
            if (SlotConstraintFilter.IsValidAssignment(
                    candidate, blocker.Date, blocker.ShiftTypeIndex, blocker.ShiftRefId,
                    blocker.TotalHours, wizard, stateTokens, blocker.StartAt, blocker.EndAt,
                    relaxation))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// The rung a receiver of a relocated shift is filtered with. The widest rung is reserved for the
    /// open slot; a receiver stays block-ideal-abiding, which keeps the blast radius of one escalated
    /// move at a single package and leaves the candidate set of the rung a superset of the one below.
    /// </summary>
    /// <param name="relaxation">Rung the open slot is being filled on</param>
    private static SlotRelaxation ReceiverRelaxation(SlotRelaxation relaxation)
        => relaxation == SlotRelaxation.All ? SlotRelaxation.RestDaysOnly : relaxation;

    /// <summary>
    /// Reports a relocation that only the widest rung made possible.
    /// </summary>
    /// <param name="escalations">Diagnostics sink; null switches the reporting off</param>
    /// <param name="relaxation">Rung the relocation succeeded on</param>
    /// <param name="agentId">Agent that took the open slot</param>
    /// <param name="slotDate">Day of the staffed slot</param>
    private static void ReportRelocationEscalation(
        Action<string>? escalations, SlotRelaxation relaxation, string agentId, DateOnly slotDate)
    {
        if (relaxation != SlotRelaxation.All)
        {
            return;
        }

        ReportEscalation(escalations, agentId, slotDate, RelocatedFill);
    }

    /// <summary>
    /// Emits one line per slot that only the widest rung of the escalation could staff.
    /// </summary>
    /// <param name="escalations">Diagnostics sink; null switches the reporting off</param>
    /// <param name="agentId">Agent that took the slot</param>
    /// <param name="slotDate">Day of the staffed slot</param>
    /// <param name="mode">How the slot was taken: directly or after a relocation</param>
    private static void ReportEscalation(
        Action<string>? escalations, string agentId, DateOnly slotDate, string mode)
        => escalations?.Invoke(
            $"{agentId} {slotDate.ToString(EscalationDateFormat, CultureInfo.InvariantCulture)} {mode}");

    private static CoreScenario RepairOverSupply(TokenOperatorContext context, ConstraintViolation violation)
    {
        var tokens = context.Primary.Tokens.ToList();

        if (!violation.ShiftRefId.HasValue || !violation.Date.HasValue)
        {
            return TokenSwapMutation.CloneScenario(context.Primary, tokens);
        }

        var candidates = tokens
            .Where(t => !t.IsLocked
                && t.ShiftRefId == violation.ShiftRefId.Value
                && t.Date == violation.Date.Value)
            .ToList();

        if (candidates.Count == 0)
        {
            return TokenSwapMutation.CloneScenario(context.Primary, tokens);
        }

        var doomed = RosterPositionBias.PickWithBottomBias(candidates, t => t.AgentId, context.Wizard.Agents, context.Rng);
        var remaining = tokens.Where(t => t != doomed).ToList();
        return TokenSwapMutation.CloneScenario(context.Primary, remaining);
    }

    /// <summary>
    /// Resolves the slot definition from the frozen per-context index instead of scanning every shift
    /// and formatting strings on each call. First match wins, as in the former linear search.
    /// </summary>
    private static CoreShift? FindSlot(CoreWizardContext context, Guid shiftRefId, DateOnly date)
        => EvaluationContext.For(context).SlotsByKey.GetValueOrDefault((shiftRefId, date));

    private static bool MatchesViolation(CoreToken token, ConstraintViolation violation)
    {
        if (!string.IsNullOrEmpty(violation.AgentId) && token.AgentId != violation.AgentId)
        {
            return false;
        }

        if (violation.Date.HasValue && token.Date != violation.Date.Value)
        {
            return false;
        }

        if (violation.TokenBlockId.HasValue && token.BlockId != violation.TokenBlockId.Value)
        {
            return false;
        }

        return true;
    }
}
