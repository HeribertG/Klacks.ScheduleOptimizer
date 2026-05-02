// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction.Fuzzy;

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
        var consumedThisBlock = state.CurrentBlockLength * 8.0;
        var blockHunger = Math.Max(0, BlockSaturationHours - consumedThisBlock);

        var blockMaturity = state.CurrentBlockLength;

        var weeklyCap = agent.MaxWeeklyHours > 0 ? agent.MaxWeeklyHours : 50.0;
        var weeklyLoad = (agent.CurrentHours + state.HoursAssignedThisRun) / weeklyCap;

        var indexBonus = ResolveIndexBonus(agent, context);

        return new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["BlockHunger"] = blockHunger,
            ["BlockMaturity"] = blockMaturity,
            ["DaysSinceEarly"] = ToDouble(state.DaysSinceShiftType, 0),
            ["DaysSinceLate"] = ToDouble(state.DaysSinceShiftType, 1),
            ["DaysSinceNight"] = ToDouble(state.DaysSinceShiftType, 2),
            ["WeeklyLoad"] = weeklyLoad,
            ["IndexBonus"] = indexBonus,
        };
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
