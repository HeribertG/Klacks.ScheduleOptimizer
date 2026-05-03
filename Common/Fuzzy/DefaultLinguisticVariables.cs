// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Common.Fuzzy;

/// <summary>
/// Phase-2 default linguistic variables for the bidding agent.
/// All membership functions are tuned around a 36h block-saturation point and a 5-day max block.
/// Values can be overridden via JSON rule-base in later phases.
/// </summary>
public static class DefaultLinguisticVariables
{
    public static IReadOnlyDictionary<string, LinguisticVariable> BuildInputs()
    {
        var dict = new Dictionary<string, LinguisticVariable>(StringComparer.Ordinal);

        dict["BlockHunger"] = new("BlockHunger", new Dictionary<string, MembershipFunction>
        {
            ["Sated"] = new TrapezoidMf(0, 0, 8, 16),
            ["Moderate"] = new TriangularMf(8, 18, 28),
            ["Hungry"] = new TriangularMf(20, 30, 36),
            ["Starving"] = new TrapezoidMf(30, 36, 60, 60),
        });

        dict["BlockMaturity"] = new("BlockMaturity", new Dictionary<string, MembershipFunction>
        {
            ["Empty"] = new TrapezoidMf(0, 0, 0.5, 1),
            ["Building"] = new TriangularMf(0.5, 1.5, 3),
            ["MidRun"] = new TriangularMf(2, 3.5, 5),
            ["Closing"] = new TrapezoidMf(4, 5, 6, 6),
        });

        dict["DaysSinceEarly"] = DaysSinceVariable("DaysSinceEarly");
        dict["DaysSinceLate"] = DaysSinceVariable("DaysSinceLate");
        dict["DaysSinceNight"] = DaysSinceVariable("DaysSinceNight");

        dict["WeeklyLoad"] = new("WeeklyLoad", new Dictionary<string, MembershipFunction>
        {
            ["Light"] = new TrapezoidMf(0, 0, 0.3, 0.5),
            ["Balanced"] = new TriangularMf(0.3, 0.6, 0.9),
            ["Heavy"] = new TrapezoidMf(0.7, 0.9, 1.2, 1.2),
        });

        dict["IndexBonus"] = new("IndexBonus", new Dictionary<string, MembershipFunction>
        {
            ["Top"] = new TrapezoidMf(0, 0, 0.15, 0.35),
            ["Mid"] = new TriangularMf(0.2, 0.5, 0.8),
            ["Bottom"] = new TrapezoidMf(0.65, 0.85, 1, 1),
        });

        return dict;
    }

    public static LinguisticVariable BuildOutput()
    {
        return new LinguisticVariable("BidScore", new Dictionary<string, MembershipFunction>
        {
            ["Reject"] = new TrapezoidMf(0, 0, 0.05, 0.15),
            ["Low"] = new TriangularMf(0.1, 0.25, 0.4),
            ["Medium"] = new TriangularMf(0.3, 0.5, 0.7),
            ["High"] = new TriangularMf(0.6, 0.75, 0.9),
            ["MustHave"] = new TrapezoidMf(0.85, 0.95, 1, 1),
        });
    }

    private static LinguisticVariable DaysSinceVariable(string name)
    {
        return new LinguisticVariable(name, new Dictionary<string, MembershipFunction>
        {
            ["Cool"] = new TrapezoidMf(0, 0, 1, 3),
            ["Warm"] = new TriangularMf(1, 4, 8),
            ["Hot"] = new TrapezoidMf(5, 9, 30, 30),
        });
    }
}
