// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Constraints;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// Repair operator: resolves a random hard-constraint violation.
/// For token-bound violations (MaxDailyHours, keyword, etc.) the operator removes an offending
/// non-locked token. For slot-bound <see cref="ViolationKind.UnderSupply"/> violations it tries
/// to ADD a valid token filling the missing slot. Locked tokens are never mutated.
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
            return RepairUnderSupply(context, pick);
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
                var attempt = RepairUnderSupply(new TokenOperatorContext(current, null, context, rng), violation);
                if (attempt.Tokens.Count > current.Tokens.Count)
                {
                    current = attempt;
                    progress = true;
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

        var doomed = candidates[context.Rng.Next(candidates.Count)];
        var remaining = context.Primary.Tokens.Where(t => t != doomed).ToList();
        return TokenSwapMutation.CloneScenario(context.Primary, remaining);
    }

    private static CoreScenario RepairUnderSupply(TokenOperatorContext context, ConstraintViolation violation)
    {
        var tokens = context.Primary.Tokens.ToList();

        if (!violation.ShiftRefId.HasValue || !violation.Date.HasValue)
        {
            return TokenSwapMutation.CloneScenario(context.Primary, tokens);
        }

        var slot = FindSlot(context.Wizard, violation.ShiftRefId.Value, violation.Date.Value);
        if (slot is null)
        {
            return TokenSwapMutation.CloneScenario(context.Primary, tokens);
        }

        var capacity = Math.Max(1, slot.RequiredAssignments);
        var assigned = tokens.Count(t => t.ShiftRefId == violation.ShiftRefId.Value && t.Date == violation.Date.Value);
        if (assigned >= capacity)
        {
            return TokenSwapMutation.CloneScenario(context.Primary, tokens);
        }

        var start = TimeOnly.TryParse(slot.StartTime, out var parsedStart) ? parsedStart : new TimeOnly(8, 0);
        var end = TimeOnly.TryParse(slot.EndTime, out var parsedEnd) ? parsedEnd : start.AddHours(8);
        var shiftTypeIndex = ShiftTypeInference.FromStartTime(start);
        var slotHours = (decimal)slot.Hours;
        var slotStartUtc = violation.Date.Value.ToDateTime(start);
        var slotEndUtc = end <= start ? violation.Date.Value.AddDays(1).ToDateTime(end) : violation.Date.Value.ToDateTime(end);

        var occupiedAgents = tokens
            .Where(t => t.ShiftRefId == violation.ShiftRefId.Value && t.Date == violation.Date.Value)
            .Select(t => t.AgentId)
            .ToHashSet(StringComparer.Ordinal);

        var candidates = context.Wizard.Agents
            .Where(agent => !occupiedAgents.Contains(agent.Id)
                && SlotConstraintFilter.IsValidAssignment(
                    agent, violation.Date.Value, shiftTypeIndex, slotHours, context.Wizard, tokens, slotStartUtc, slotEndUtc))
            .ToList();

        if (candidates.Count == 0)
        {
            return TokenSwapMutation.CloneScenario(context.Primary, tokens);
        }

        var chosen = candidates[context.Rng.Next(candidates.Count)];
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
            AgentId: chosen.Id));

        return TokenSwapMutation.CloneScenario(context.Primary, tokens);
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

        var doomed = candidates[context.Rng.Next(candidates.Count)];
        var remaining = tokens.Where(t => t != doomed).ToList();
        return TokenSwapMutation.CloneScenario(context.Primary, remaining);
    }

    private static CoreShift? FindSlot(CoreWizardContext context, Guid shiftRefId, DateOnly date)
    {
        var targetDate = date.ToString("yyyy-MM-dd");
        var targetId = shiftRefId.ToString();
        foreach (var shift in context.Shifts)
        {
            if (shift.Date == targetDate && shift.Id == targetId)
            {
                return shift;
            }
        }

        return null;
    }

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
