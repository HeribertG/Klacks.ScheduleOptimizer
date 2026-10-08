// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction.Controller;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Diagnostics;

/// <summary>
/// Reports the slots a finished plan leaves understaffed and why. The demand of a (shift, date) key is the sum of
/// its rows (a shift with several seats arrives as several rows with the same id, exactly as the fitness counts it).
/// Every agent is checked twice against the Stage-0 hard rules: first against the fixed facts only (locked tokens;
/// contracts, absences, keywords, qualifications, existing works and boundary works come from the context), which
/// tells an unsolvable slot (no agent eligible) from one a different plan could fill; then, for the eligible agents,
/// against the finished plan, which tells who could take the slot without changing anything else. Two passes are
/// needed because a verdict names only the FIRST failing rule and the checker tests a plan rule (MaxConsecutiveDays)
/// before the contract day, so sorting single verdicts into plan-dependent and plan-independent rules would misfile
/// an agent that fails both.
/// </summary>
public static class UnfilledSlotDiagnostics
{
    /// <summary>Upper bound of diagnosed slots per run, so a badly understaffed period cannot blow up the result.</summary>
    public const int DefaultMaxSlots = 200;

    private const string IsoDateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Diagnoses the understaffed slots of <paramref name="plan"/>, earliest first.
    /// </summary>
    /// <param name="context">Engine input the plan was built from</param>
    /// <param name="plan">Finished plan, locked tokens included</param>
    /// <param name="maxSlots">Maximum number of slots to diagnose</param>
    /// <param name="cancellationToken">Checked once per slot</param>
    public static IReadOnlyList<UnfilledSlotDiagnosis> Diagnose(
        CoreWizardContext context,
        IReadOnlyList<CoreToken> plan,
        int maxSlots = DefaultMaxSlots,
        CancellationToken cancellationToken = default)
    {
        if (maxSlots <= 0)
        {
            return [];
        }

        var demand = new Dictionary<(Guid ShiftId, DateOnly Date), (CoreShift Row, int Seats)>();
        foreach (var shift in context.Shifts)
        {
            if (!Guid.TryParse(shift.Id, out var shiftId)
                || !DateOnly.TryParseExact(shift.Date, IsoDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            var seats = Math.Max(1, shift.RequiredAssignments);
            demand[(shiftId, date)] = demand.TryGetValue((shiftId, date), out var known)
                ? (known.Row, known.Seats + seats)
                : (shift, seats);
        }

        var assigned = new Dictionary<(Guid ShiftId, DateOnly Date), int>();
        foreach (var token in plan)
        {
            var key = (token.ShiftRefId, token.Date);
            assigned[key] = assigned.GetValueOrDefault(key) + 1;
        }

        var understaffed = demand
            .Select(entry => (Key: entry.Key, entry.Value.Row, Missing: entry.Value.Seats - assigned.GetValueOrDefault(entry.Key)))
            .Where(slot => slot.Missing > 0)
            .OrderBy(slot => slot.Key.Date)
            .ThenBy(slot => slot.Row.StartTime, StringComparer.Ordinal)
            .ThenBy(slot => slot.Key.ShiftId)
            .Take(maxSlots)
            .ToList();

        var fixedFacts = plan.Where(token => token.IsLocked).ToList();
        var stage0 = new Stage0HardConstraintChecker();
        var result = new List<UnfilledSlotDiagnosis>(understaffed.Count);
        foreach (var slot in understaffed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var eligibilityVetoes = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var placementVetoes = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var eligible = 0;
            var placeable = 0;
            foreach (var agent in context.Agents)
            {
                var eligibilityVerdict = stage0.Check(agent, slot.Row, fixedFacts, context);
                if (eligibilityVerdict is not null)
                {
                    Count(eligibilityVetoes, eligibilityVerdict.RuleName);
                    continue;
                }

                eligible++;
                var placementVerdict = stage0.Check(agent, slot.Row, plan, context);
                if (placementVerdict is not null)
                {
                    Count(placementVetoes, placementVerdict.RuleName);
                    continue;
                }

                placeable++;
            }

            result.Add(new UnfilledSlotDiagnosis(
                slot.Key.ShiftId, slot.Key.Date, slot.Missing, eligible, placeable, eligibilityVetoes, placementVetoes));
        }

        return result;
    }

    private static void Count(IDictionary<string, int> counts, string ruleName)
        => counts[ruleName] = counts.TryGetValue(ruleName, out var known) ? known + 1 : 1;
}
