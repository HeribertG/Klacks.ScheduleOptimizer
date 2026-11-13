// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Rotation start of the initial population. Where a <see cref="RotationWheel"/> applies it lays the wheel: locked works
/// and the carry-in continuation first, then every slot in calendar order (date, start time, id - a five-night block is
/// only accepted by the block-length ideal in that order), every candidate checked by <see cref="SlotConstraintFilter"/>.
/// The seat holder whose row expects exactly this kind on this day wins even above its guaranteed hours; then fillers
/// and seat holders expecting work of another kind; seat holders on a free day or a hole and placeholders (stand-ins)
/// last, so a placeholder never takes a place from a real person because of the wheel. Within a tier the
/// coverage-first order applies (below guaranteed hours first, largest remaining target, motivation, roster position);
/// unlike the canon start there is no rotation preference for fillers, the fitness judge rates their rotation.
/// It only takes part where a wheel applies (<see cref="AppliesTo"/>); Wizard 1 uses it in its second run only
/// (<see cref="TokenEvolutionLoop"/>).
/// </summary>
public sealed class RotationWheelTokenStrategy : IConditionalTokenPopulationStrategy
{
    private const int TierMatch = 2;
    private const int TierNeutral = 1;
    private const int TierOff = 0;
    private const double Tolerance = 1e-9;
    private static readonly TimeOnly FallbackStart = new(8, 0);

    public bool AppliesTo(CoreWizardContext context) => RotationWheel.Build(context) is not null;

    public CoreScenario BuildScenario(CoreWizardContext context, Random rng)
    {
        ArgumentNullException.ThrowIfNull(context);
        var wheel = RotationWheel.Build(context)
            ?? throw new InvalidOperationException("No rotation wheel applies to the context; check AppliesTo first.");
        return Lay(context, wheel);
    }

    private static CoreScenario Lay(CoreWizardContext context, RotationWheel wheel)
    {
        var tokens = new List<CoreToken>(
            LockedTokenFactory.BuildLockedTokens(context.LockedWorks, context.SchedulingMaxConsecutiveDays));
        var seed = CarryInContinuationSeeder.Seed(context, tokens);
        var hoursByAgent = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            hoursByAgent[token.AgentId] = hoursByAgent.GetValueOrDefault(token.AgentId) + (double)(token.TotalHours + token.Surcharges);
        }

        var orderedSlots = context.Shifts
            .OrderBy(s => s.Date, StringComparer.Ordinal)
            .ThenBy(s => s.StartTime, StringComparer.Ordinal)
            .ThenBy(s => s.Id, StringComparer.Ordinal);
        foreach (var slot in orderedSlots)
        {
            if (seed.Occupancy.TryClaim(slot) || !DateOnly.TryParse(slot.Date, CultureInfo.InvariantCulture, out var date))
            {
                continue;
            }

            var start = TimeOnly.TryParse(slot.StartTime, CultureInfo.InvariantCulture, out var parsedStart) ? parsedStart : FallbackStart;
            var end = TimeOnly.TryParse(slot.EndTime, CultureInfo.InvariantCulture, out var parsedEnd) ? parsedEnd : start.AddHours(slot.Hours);
            var kind = ShiftTypeInference.FromSpan(start, end);
            var hours = (decimal)slot.Hours;
            var shiftRefId = Guid.TryParse(slot.Id, out var parsedShift) ? parsedShift : Guid.Empty;
            var slotStart = date.ToDateTime(start);
            var slotEnd = end <= start ? date.AddDays(1).ToDateTime(end) : date.ToDateTime(end);

            var agent = SelectAgent(context, wheel, tokens, hoursByAgent, date, kind, hours, shiftRefId, slotStart, slotEnd);
            if (agent is null)
            {
                continue;
            }

            var surcharges = SurchargeEstimator.Estimate(hours, kind, date, agent);
            tokens.Add(new CoreToken(
                WorkIds: [],
                ShiftTypeIndex: kind,
                Date: date,
                TotalHours: hours,
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
            hoursByAgent[agent.Id] = hoursByAgent.GetValueOrDefault(agent.Id) + (double)(hours + surcharges);
        }

        return new CoreScenario { Id = Guid.NewGuid().ToString(), Tokens = tokens };
    }

    private static CoreAgent? SelectAgent(
        CoreWizardContext context,
        RotationWheel wheel,
        IReadOnlyList<CoreToken> tokens,
        IReadOnlyDictionary<string, double> hoursByAgent,
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

            var assigned = agent.CurrentHours + hoursByAgent.GetValueOrDefault(agent.Id);
            var below = agent.GuaranteedHours > 0 && assigned < agent.GuaranteedHours;
            var expected = wheel.Expected(agent.Id, date);
            var tier = expected == kind ? TierMatch
                : !agent.IsStandIn && (expected is null || expected >= 0) ? TierNeutral
                : TierOff;
            var key = new Candidate(
                tier,
                below,
                below ? agent.GuaranteedHours - assigned : 0,
                below ? MotivationFormula.Compute(agent, shiftRefId, hours, context.ShiftPreferences) : 0,
                below ? -index : index);
            if (best is null || key.IsBetterThan(bestKey))
            {
                best = agent;
                bestKey = key;
            }
        }

        return best;
    }

    /// <summary>Lexicographic selection key; earlier fields dominate later ones.</summary>
    private readonly record struct Candidate(int Tier, bool BelowTarget, double Remaining, double Motivation, int RosterKey)
    {
        public bool IsBetterThan(Candidate other)
        {
            if (Tier != other.Tier)
            {
                return Tier > other.Tier;
            }

            if (BelowTarget != other.BelowTarget)
            {
                return BelowTarget;
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
