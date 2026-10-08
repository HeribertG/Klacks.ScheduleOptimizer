// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Auction.Controller;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Diagnostics;

/// <summary>
/// Reports the slots a finished plan leaves understaffed and why. The demand of a (shift, date) key is the sum of
/// its rows (a shift with several seats arrives as several rows with the same id, exactly as the fitness counts it);
/// every agent is then checked against the Stage-0 hard rules on the finished plan, so the caller can tell an
/// unsolvable slot (no agent may legally take it) from one the engine merely left open.
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
    public static IReadOnlyList<UnfilledSlotDiagnosis> Diagnose(
        CoreWizardContext context,
        IReadOnlyList<CoreToken> plan,
        int maxSlots = DefaultMaxSlots)
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

        var stage0 = new Stage0HardConstraintChecker();
        var result = new List<UnfilledSlotDiagnosis>(understaffed.Count);
        foreach (var slot in understaffed)
        {
            var vetoCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var feasible = 0;
            foreach (var agent in context.Agents)
            {
                var verdict = stage0.Check(agent, slot.Row, plan, context);
                if (verdict is null)
                {
                    feasible++;
                    continue;
                }

                vetoCounts[verdict.RuleName] = vetoCounts.GetValueOrDefault(verdict.RuleName) + 1;
            }

            result.Add(new UnfilledSlotDiagnosis(slot.Key.ShiftId, slot.Key.Date, slot.Missing, feasible, vetoCounts));
        }

        return result;
    }
}
