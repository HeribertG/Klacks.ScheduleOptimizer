// Copyright (c) Heribert Gasparoli Private. All rights reserved.

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
/// </summary>
public sealed class TokenRepair : ITokenOperator
{
    private readonly TokenConstraintChecker _checker;

    public TokenRepair(TokenConstraintChecker checker)
    {
        _checker = checker;
    }

    public CoreScenario Apply(TokenOperatorContext context)
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
            return TryRepairUnderSupply(context.Primary, context.Wizard, context.Rng, pick, out var repaired)
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
    public CoreScenario FillAllUnderSupply(
        CoreScenario scenario,
        CoreWizardContext context,
        Random rng,
        CancellationToken cancellationToken = default,
        Action<string>? trace = null)
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

                if (TryRepairUnderSupply(current, context, rng, violation, out var repaired))
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
    /// <param name="repaired">The repaired scenario, valid only when the method returns true.</param>
    private static bool TryRepairUnderSupply(
        CoreScenario primary,
        CoreWizardContext wizard,
        Random rng,
        ConstraintViolation violation,
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
        var shiftTypeIndex = ShiftTypeInference.FromStartTime(start);
        var slotHours = (decimal)slot.Hours;
        var slotStartUtc = violation.Date.Value.ToDateTime(start);
        var slotEndUtc = end <= start ? violation.Date.Value.AddDays(1).ToDateTime(end) : violation.Date.Value.ToDateTime(end);

        var occupiedAgents = primary.Tokens
            .Where(t => t.ShiftRefId == violation.ShiftRefId.Value && t.Date == violation.Date.Value)
            .Select(t => t.AgentId)
            .ToHashSet(StringComparer.Ordinal);

        var candidates = wizard.Agents
            .Where(agent => !occupiedAgents.Contains(agent.Id)
                && SlotConstraintFilter.IsValidAssignment(
                    agent, violation.Date.Value, shiftTypeIndex, violation.ShiftRefId.Value, slotHours, wizard, primary.Tokens, slotStartUtc, slotEndUtc))
            .ToList();

        if (candidates.Count == 0)
        {
            return false;
        }

        var chosen = RosterPositionBias.PickAccuracyAware(candidates, primary.Tokens, wizard.Agents, rng);
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
            LocationContext: null,
            ShiftRefId: violation.ShiftRefId.Value,
            AgentId: chosen.Id)
        {
            Surcharges = SurchargeEstimator.Estimate(slotHours, shiftTypeIndex, violation.Date.Value, chosen),
        });

        repaired = TokenSwapMutation.CloneScenario(primary, tokens);
        return true;
    }

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
