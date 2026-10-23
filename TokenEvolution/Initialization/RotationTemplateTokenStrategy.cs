// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Rotation start (canon start) for the initial population. Works like <see cref="CoverageFirstTokenStrategy"/> - every
/// (shift, date) slot in deterministic order, every candidate checked by <see cref="SlotConstraintFilter"/> - but asks the
/// <see cref="RotationTemplate"/> first: among the valid agents, the one whose canon expects exactly this shift kind on
/// this day wins, then one whose canon expects work of another kind, then everybody else. Within a tier the coverage-first
/// order applies (below guaranteed hours before surplus, rotation-respecting before rotation-breaking, largest remaining
/// target, motivation, roster position). So the plan follows the canon wherever the rules allow it and falls back to the
/// ordinary coverage-first choice wherever they do not; coverage is never lower than coverage-first would reach for the
/// same slot. Without a template (no shift demand, fewer than two shift workers) it behaves exactly like coverage-first.
/// <para>
/// It only seeds where a canon can carry the plan (<see cref="AppliesTo"/>): when the canon on its own staffs at least
/// <see cref="MinCoverageFit"/> of the seats. A small team whose canon leaves seats open gains nothing from the canon start
/// and only loses diversity (measured on the carry-in scenario 2: same means, worse single seeds), so there the
/// population stays exactly as before.
/// </para>
/// </summary>
public sealed class RotationTemplateTokenStrategy : IConditionalTokenPopulationStrategy
{
    /// <summary>Minimum <see cref="RotationTemplate.CoverageFit"/> for the canon start to take part in the population.</summary>
    public const double MinCoverageFit = 0.95;

    private const int TemplateMatch = 2;
    private const int TemplateWork = 1;
    private const int TemplateFree = 0;
    private const double Tolerance = 1e-9;

    public bool AppliesTo(CoreWizardContext context)
        => RotationTemplate.Build(context) is { CoverageFit: >= MinCoverageFit };

    public CoreScenario BuildScenario(CoreWizardContext context, Random rng)
    {
        ArgumentNullException.ThrowIfNull(context);
        var template = RotationTemplate.Build(context);
        var tokens = new List<CoreToken>(
            LockedTokenFactory.BuildLockedTokens(context.LockedWorks, context.SchedulingMaxConsecutiveDays));
        var seed = CarryInContinuationSeeder.Seed(context, tokens);

        var hoursByAgent = new Dictionary<string, double>(StringComparer.Ordinal);
        var lastBlock = new Dictionary<string, (DateOnly LastDate, int StartKind)>(StringComparer.Ordinal);
        foreach (var locked in tokens.OrderBy(t => t.Date))
        {
            AddHours(hoursByAgent, locked.AgentId, (double)(locked.TotalHours + locked.Surcharges));
            TrackBlock(lastBlock, locked.AgentId, locked.Date, locked.ShiftTypeIndex);
        }

        var orderedSlots = context.Shifts
            .OrderBy(s => s.Date, StringComparer.Ordinal)
            .ThenBy(s => s.StartTime, StringComparer.Ordinal)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();

        foreach (var slot in orderedSlots)
        {
            if (seed.Occupancy.TryClaim(slot) || !DateOnly.TryParse(slot.Date, out var slotDate))
            {
                continue;
            }

            var start = TimeOnly.TryParse(slot.StartTime, out var parsedStart) ? parsedStart : new TimeOnly(8, 0);
            var end = TimeOnly.TryParse(slot.EndTime, out var parsedEnd) ? parsedEnd : start.AddHours(slot.Hours);
            var kind = ShiftTypeInference.FromSpan(start, end);
            var slotHours = (decimal)slot.Hours;
            var shiftRefId = Guid.TryParse(slot.Id, out var parsedShift) ? parsedShift : Guid.Empty;
            var slotStart = slotDate.ToDateTime(start);
            var slotEnd = end <= start ? slotDate.AddDays(1).ToDateTime(end) : slotDate.ToDateTime(end);

            var agent = SelectAgent(context, template, tokens, hoursByAgent, lastBlock, slotDate, kind, slotHours, shiftRefId, slotStart, slotEnd);
            if (agent is null)
            {
                continue;
            }

            var surcharges = SurchargeEstimator.Estimate(slotHours, kind, slotDate, agent);
            tokens.Add(new CoreToken(
                WorkIds: [],
                ShiftTypeIndex: kind,
                Date: slotDate,
                TotalHours: slotHours,
                StartAt: slotStart,
                EndAt: slotEnd,
                BlockId: Guid.NewGuid(),
                PositionInBlock: 0,
                IsLocked: false,
                LocationContext: slot.LocationContext,
                ShiftRefId: shiftRefId,
                AgentId: agent.Id)
            {
                Surcharges = surcharges,
            });
            AddHours(hoursByAgent, agent.Id, (double)(slotHours + surcharges));
            TrackBlock(lastBlock, agent.Id, slotDate, kind);
        }

        return new CoreScenario
        {
            Id = Guid.NewGuid().ToString(),
            Tokens = tokens,
        };
    }

    private static CoreAgent? SelectAgent(
        CoreWizardContext context,
        RotationTemplate? template,
        IReadOnlyList<CoreToken> tokens,
        IReadOnlyDictionary<string, double> hoursByAgent,
        IReadOnlyDictionary<string, (DateOnly LastDate, int StartKind)> lastBlock,
        DateOnly date,
        int kind,
        decimal hours,
        Guid shiftRefId,
        DateTime slotStart,
        DateTime slotEnd)
    {
        CoreAgent? best = null;
        Candidate bestKey = default;
        for (var index = 0; index < context.Agents.Count; index++)
        {
            var agent = context.Agents[index];
            if (!SlotConstraintFilter.IsValidAssignment(agent, date, kind, shiftRefId, hours, context, tokens, slotStart, slotEnd))
            {
                continue;
            }

            var assigned = agent.CurrentHours + hoursByAgent.GetValueOrDefault(agent.Id, 0);
            var below = agent.GuaranteedHours > 0 && assigned < agent.GuaranteedHours;
            var expected = template?.Expected(agent.Id, date);
            var templateTier = expected == kind ? TemplateMatch
                : expected is >= 0 ? TemplateWork
                : TemplateFree;
            var key = new Candidate(
                below,
                templateTier,
                !ViolatesRotation(agent, lastBlock, date, kind),
                below ? agent.GuaranteedHours - assigned : 0,
                below ? MotivationFormula.Compute(agent, shiftRefId, hours, context.ShiftPreferences) : 0,
                // Below the target the top-most roster position wins ties; in the surplus the bottom-most eats what is left.
                below ? -index : index);
            if (best is null || key.IsBetterThan(bestKey))
            {
                best = agent;
                bestKey = key;
            }
        }

        return best;
    }

    /// <summary>Same rotation rule as coverage-first: a new block must not repeat the previous block's kind.</summary>
    private static bool ViolatesRotation(
        CoreAgent agent,
        IReadOnlyDictionary<string, (DateOnly LastDate, int StartKind)> lastBlock,
        DateOnly date,
        int kind)
    {
        if (!agent.PerformsShiftWork || !lastBlock.TryGetValue(agent.Id, out var block))
        {
            return false;
        }

        var startsNewBlock = date.DayNumber - block.LastDate.DayNumber > 1;
        return startsNewBlock && block.StartKind == kind;
    }

    private static void TrackBlock(Dictionary<string, (DateOnly LastDate, int StartKind)> lastBlock, string agentId, DateOnly date, int kind)
    {
        if (!lastBlock.TryGetValue(agentId, out var block) || date.DayNumber - block.LastDate.DayNumber > 1)
        {
            lastBlock[agentId] = (date, kind);
            return;
        }

        lastBlock[agentId] = block with { LastDate = date };
    }

    private static void AddHours(Dictionary<string, double> hoursByAgent, string agentId, double hours)
        => hoursByAgent[agentId] = hoursByAgent.GetValueOrDefault(agentId, 0) + hours;

    /// <summary>Lexicographic selection key; earlier fields dominate later ones.</summary>
    private readonly record struct Candidate(
        bool BelowTarget,
        int TemplateTier,
        bool RotationOk,
        double Remaining,
        double Motivation,
        int RosterKey)
    {
        public bool IsBetterThan(Candidate other)
        {
            if (BelowTarget != other.BelowTarget)
            {
                return BelowTarget;
            }

            if (TemplateTier != other.TemplateTier)
            {
                return TemplateTier > other.TemplateTier;
            }

            if (RotationOk != other.RotationOk)
            {
                return RotationOk;
            }

            if (Math.Abs(Remaining - other.Remaining) > Tolerance)
            {
                return Remaining > other.Remaining;
            }

            if (Math.Abs(Motivation - other.Motivation) > Tolerance)
            {
                return Motivation > other.Motivation;
            }

            return RosterKey > other.RosterKey;
        }
    }
}
