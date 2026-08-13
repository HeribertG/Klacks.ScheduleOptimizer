// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

/// <summary>
/// Roulette-wheel selection helper for GA operators that mutate slot count per agent.
/// Top-bias prefers agents earlier in the roster (intended top-down distribution),
/// bottom-bias prefers agents later in the roster (used when removing tokens so that
/// surplus slots are taken away from the bottom first).
/// </summary>
/// <param name="candidates">Selection pool — must contain at least one element.</param>
/// <param name="agentIdOf">Extracts the agent id from a candidate so the helper can find its roster position.</param>
/// <param name="roster">Authoritative ordered agent list; index 0 is top, last is bottom.</param>
/// <param name="rng">RNG instance owned by the caller for reproducibility.</param>
public static class RosterPositionBias
{
    public static T PickWithTopBias<T>(
        IReadOnlyList<T> candidates,
        Func<T, string> agentIdOf,
        IReadOnlyList<CoreAgent> roster,
        Random rng)
    {
        return Pick(candidates, agentIdOf, roster, rng, inverse: false);
    }

    public static T PickWithBottomBias<T>(
        IReadOnlyList<T> candidates,
        Func<T, string> agentIdOf,
        IReadOnlyList<CoreAgent> roster,
        Random rng)
    {
        return Pick(candidates, agentIdOf, roster, rng, inverse: true);
    }

    /// <summary>
    /// Accuracy-aware roster pick implementing the top-down rule for hour-adding moves:
    /// candidates still below their guaranteed hours receive first (top roster position
    /// preferred); once every candidate is at or above target the surplus goes to the
    /// bottom of the roster, keeping the top accurate ("the bottom eats what is left").
    /// <para>
    /// The optional <paramref name="preferWhere"/> narrows the chosen accuracy group to the
    /// candidates it matches, when there are any — deliberately INSIDE the accuracy order, never
    /// across it: rule 5 (hours flow top-down) outranks whatever the preference expresses. The
    /// package-aware repair of SPEC.md decision 13 passes "extends an existing package" here.
    /// </para>
    /// </summary>
    /// <param name="candidates">Valid receiving agents — must contain at least one element.</param>
    /// <param name="assignedTokens">Tokens currently assigned in the scenario (hours source).</param>
    /// <param name="roster">Authoritative ordered agent list; index 0 is top.</param>
    /// <param name="rng">RNG instance owned by the caller for reproducibility.</param>
    /// <param name="preferWhere">Optional tie-breaking preference applied inside the accuracy group.</param>
    /// <param name="balanceNightShare">
    /// M11 (rule 9, 2026-08-13): when the slot being given away is a NIGHT, narrow the pool to the
    /// candidates currently holding the fewest nights — applied after the accuracy group (rule 5)
    /// and after <paramref name="preferWhere"/> (rule 6), so the higher-ranked rules stay in
    /// charge and fairness only breaks the remaining tie. Without this the ruin-recreate rebuild
    /// handed the nights of a whole window to the same agents over and over (Sz3: one agent at 19
    /// of 31 nights, cohort spread 0.45), and no later pass could dissolve the hoard because
    /// single-day fairness swaps are edge-illegal on compact plans (rejection census 2026-08-13).
    /// </param>
    public static CoreAgent PickAccuracyAware(
        IReadOnlyList<CoreAgent> candidates,
        IReadOnlyList<CoreToken> assignedTokens,
        IReadOnlyList<CoreAgent> roster,
        Random rng,
        Func<CoreAgent, bool>? preferWhere = null,
        bool balanceNightShare = false)
    {
        var hoursByAgent = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var token in assignedTokens)
        {
            var hours = (double)(token.TotalHours + token.Surcharges);
            hoursByAgent[token.AgentId] = hoursByAgent.TryGetValue(token.AgentId, out var existing)
                ? existing + hours
                : hours;
        }

        var belowTarget = candidates
            .Where(a => a.GuaranteedHours > 0
                && a.CurrentHours + hoursByAgent.GetValueOrDefault(a.Id, 0) < a.GuaranteedHours)
            .ToList();

        IReadOnlyList<CoreAgent> pool = belowTarget.Count > 0 ? belowTarget : candidates;
        if (preferWhere is not null)
        {
            var preferred = pool.Where(preferWhere).ToList();
            if (preferred.Count > 0)
            {
                pool = preferred;
            }
        }

        // The tie-break also runs inside the rule-6 preference group: restricting it to
        // preference-free picks (tried as M11c on 2026-08-13) surrendered the whole night-fairness
        // gain, because on compact plans nearly every fill has extenders. The one candidate the
        // break must never evict — the owner of an open carried-in package — is protected UPSTREAM
        // by the deterministic continuation priority of the repair fill (rules A10/A11), so the
        // conflict measured as M11 cannot reoccur here.
        if (balanceNightShare && pool.Count > 1)
        {
            var nightsByAgent = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var token in assignedTokens)
            {
                if (token.ShiftTypeIndex == NightShiftTypeIndex)
                {
                    nightsByAgent[token.AgentId] = nightsByAgent.GetValueOrDefault(token.AgentId) + 1;
                }
            }

            var fewestNights = pool.Min(a => nightsByAgent.GetValueOrDefault(a.Id));
            pool = pool.Where(a => nightsByAgent.GetValueOrDefault(a.Id) == fewestNights).ToList();
        }

        return belowTarget.Count > 0
            ? PickWithTopBias(pool, a => a.Id, roster, rng)
            : PickWithBottomBias(pool, a => a.Id, roster, rng);
    }

    /// <summary>Token shift-type index of a night shift, as the whole token engine reads it.</summary>
    internal const int NightShiftTypeIndex = 2;

    private static T Pick<T>(
        IReadOnlyList<T> candidates,
        Func<T, string> agentIdOf,
        IReadOnlyList<CoreAgent> roster,
        Random rng,
        bool inverse)
    {
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var n = roster.Count;
        var weights = new double[candidates.Count];
        var total = 0.0;
        for (var i = 0; i < candidates.Count; i++)
        {
            var pos = IndexOf(roster, agentIdOf(candidates[i]));
            if (pos < 0 || pos >= n)
            {
                pos = n - 1;
            }
            weights[i] = inverse ? (pos + 1) : (n - pos);
            total += weights[i];
        }

        if (total <= 0)
        {
            return candidates[rng.Next(candidates.Count)];
        }

        var pick = rng.NextDouble() * total;
        var cumulative = 0.0;
        for (var i = 0; i < candidates.Count; i++)
        {
            cumulative += weights[i];
            if (pick < cumulative)
            {
                return candidates[i];
            }
        }
        return candidates[^1];
    }

    private static int IndexOf(IReadOnlyList<CoreAgent> roster, string agentId)
    {
        for (var i = 0; i < roster.Count; i++)
        {
            if (roster[i].Id == agentId)
            {
                return i;
            }
        }
        return -1;
    }
}
