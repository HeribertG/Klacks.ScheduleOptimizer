// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution.Auction.Fuzzy;

/// <summary>
/// A linguistic variable holds named membership functions ("terms") over a crisp domain.
/// Example: BlockHunger with terms {Sated, Moderate, Hungry, Starving}.
/// </summary>
/// <param name="Name">Variable name, e.g. "BlockHunger"</param>
/// <param name="Terms">Map term-name → membership function</param>
public sealed record LinguisticVariable(string Name, IReadOnlyDictionary<string, MembershipFunction> Terms)
{
    public double Mu(string term, double crisp)
    {
        if (!Terms.TryGetValue(term, out var mf))
        {
            throw new ArgumentException($"Unknown term '{term}' in variable '{Name}'.");
        }
        return mf.Mu(crisp);
    }

    public IEnumerable<string> TermNames => Terms.Keys;
}
