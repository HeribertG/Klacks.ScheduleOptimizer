// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.Common.Fuzzy;
using Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Auction.Agent;

/// <summary>
/// Phase-2 bidding agent driven by Mamdani fuzzy inference.
/// Extracts crisp features from the current runtime state, fuzzifies them through the default
/// linguistic variables, fires rules from the embedded JSON rule base, and defuzzifies to a bid
/// score in [0,1]. The list of fired rules is returned for explainability.
/// </summary>
public sealed class FuzzyBiddingAgent : IBiddingAgent
{
    private const double BlockSaturationHours = 36.0;

    private readonly MamdaniInferenceEngine _engine;

    public FuzzyBiddingAgent()
        : this(BuildDefaultEngine())
    {
    }

    public FuzzyBiddingAgent(MamdaniInferenceEngine engine)
    {
        _engine = engine;
    }

    public Bid Evaluate(CoreAgent agent, CoreShift slot, AgentRuntimeState state, CoreWizardContext context)
    {
        var crispInputs = ExtractFeatures(agent, slot, state, context);
        var result = _engine.Infer(crispInputs);
        var firedNames = result.FiredRules
            .OrderByDescending(r => r.Activation)
            .Take(5)
            .Select(r => r.RuleName)
            .ToList();
        return new Bid(agent.Id, Math.Clamp(result.CrispOutput, 0.0, 1.0), firedNames);
    }

    private static Dictionary<string, double> ExtractFeatures(
        CoreAgent agent, CoreShift slot, AgentRuntimeState state, CoreWizardContext context)
    {
        // The runtime state still carries the PREVIOUS block when the slot lies after rest days.
        // Bidding must then see a fresh block (hungry + empty) — otherwise every candidate is
        // "Sated → Reject" at block restarts, all bids collapse to the floor and the roster
        // tie-break alone decides, which kills the shift-type rotation.
        var startsNewBlock = StartsNewBlock(slot, state);
        var effectiveBlockLength = startsNewBlock ? 0 : state.CurrentBlockLength;

        var consumedThisBlock = effectiveBlockLength * 8.0;
        var blockHunger = Math.Max(0, BlockSaturationHours - consumedThisBlock);

        var blockMaturity = effectiveBlockLength;

        var weeklyCap = agent.MaxWeeklyHours > 0 ? agent.MaxWeeklyHours : 50.0;
        var weeklyLoad = (agent.CurrentHours + state.HoursAssignedThisRun) / weeklyCap;

        var indexBonus = ResolveIndexBonus(agent, context);

        var slotTypeIndex = ShiftTypeInference.FromStartTimeString(slot.StartTime);

        return new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["BlockHunger"] = blockHunger,
            ["BlockMaturity"] = blockMaturity,
            ["DaysSinceEarly"] = ToDouble(state.DaysSinceShiftType, 0),
            ["DaysSinceLate"] = ToDouble(state.DaysSinceShiftType, 1),
            ["DaysSinceNight"] = ToDouble(state.DaysSinceShiftType, 2),
            ["SlotDaysSince"] = ToDouble(state.DaysSinceShiftType, slotTypeIndex),
            ["WeeklyLoad"] = weeklyLoad,
            ["IndexBonus"] = indexBonus,
            ["NewBlockSameType"] = ResolveNewBlockSameType(agent, state, slotTypeIndex, startsNewBlock),
        };
    }

    private static bool StartsNewBlock(CoreShift slot, AgentRuntimeState state)
    {
        if (!state.LastWorkedDate.HasValue)
        {
            return true;
        }

        return DateOnly.TryParse(slot.Date, out var slotDate)
            && slotDate.DayNumber - state.LastWorkedDate.Value.DayNumber > 1;
    }

    /// <summary>
    /// 1.0 when the agent would START A NEW BLOCK with the same shift type its previous block
    /// used — the rotation rule (early → late → night) demands a type change between blocks.
    /// Always 0 for agents without PerformsShiftWork: they may only work day shifts and must
    /// not be penalised for repeating them.
    /// </summary>
    private static double ResolveNewBlockSameType(
        CoreAgent agent, AgentRuntimeState state, int slotTypeIndex, bool startsNewBlock)
    {
        if (!agent.PerformsShiftWork || state.CurrentBlockStartShiftType < 0)
        {
            return 0.0;
        }

        return startsNewBlock && state.CurrentBlockStartShiftType == slotTypeIndex ? 1.0 : 0.0;
    }

    private static double ResolveIndexBonus(CoreAgent agent, CoreWizardContext context)
    {
        for (var i = 0; i < context.Agents.Count; i++)
        {
            if (context.Agents[i].Id == agent.Id)
            {
                return context.Agents.Count <= 1 ? 0.0 : (double)i / (context.Agents.Count - 1);
            }
        }
        return 1.0;
    }

    private static double ToDouble(IReadOnlyList<int> arr, int idx)
    {
        if (idx < 0 || idx >= arr.Count) return 30.0;
        var v = arr[idx];
        if (v == int.MaxValue) return 30.0;
        return v;
    }

    private static MamdaniInferenceEngine BuildDefaultEngine()
    {
        return new MamdaniInferenceEngine(
            DefaultLinguisticVariables.BuildInputs(),
            DefaultLinguisticVariables.BuildOutput(),
            RuleBaseLoader.LoadDefault());
    }
}
