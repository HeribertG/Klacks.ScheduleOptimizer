// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Seats per day and shift kind of the period (sum of <c>Max(1, RequiredAssignments)</c> over the slots, the kind
/// inferred from the slot's span like the canon template does) and the hours of one shift per kind, for
/// <see cref="RotationWheel"/>.
/// </summary>
public sealed class RotationWheelDemand
{
    private readonly Dictionary<(DateOnly Date, int Kind), int> _seats;
    private readonly Dictionary<int, double> _hours;

    private RotationWheelDemand(Dictionary<(DateOnly Date, int Kind), int> seats, Dictionary<int, double> hours)
    {
        _seats = seats;
        _hours = hours;
        Kinds = hours.Keys.Order().ToList();
    }

    /// <summary>Demanded shift kinds in the order early, late, night.</summary>
    public IReadOnlyList<int> Kinds { get; }

    /// <summary>Counts the demand of the context's slots.</summary>
    public static RotationWheelDemand Count(CoreWizardContext context)
    {
        var seats = new Dictionary<(DateOnly Date, int Kind), int>();
        var hours = new Dictionary<int, List<double>>();
        foreach (var shift in context.Shifts)
        {
            if (!DateOnly.TryParse(shift.Date, CultureInfo.InvariantCulture, out var date) || !TimeOnly.TryParse(shift.StartTime, CultureInfo.InvariantCulture, out var start))
            {
                continue;
            }

            var end = TimeOnly.TryParse(shift.EndTime, CultureInfo.InvariantCulture, out var parsedEnd) ? parsedEnd : start.AddHours(shift.Hours);
            var kind = ShiftTypeInference.FromSpan(start, end);
            seats[(date, kind)] = seats.GetValueOrDefault((date, kind)) + Math.Max(1, shift.RequiredAssignments);
            if (!hours.TryGetValue(kind, out var list))
            {
                hours[kind] = list = [];
            }

            list.Add(shift.Hours);
        }

        return new RotationWheelDemand(seats, hours.ToDictionary(p => p.Key, p => p.Value.Average()));
    }

    /// <summary>Seats of the kind on the date.</summary>
    public int Seats(DateOnly date, int kind) => _seats.GetValueOrDefault((date, kind));

    /// <summary>Smallest number of seats of the kind on any of the days.</summary>
    public int MinDaily(int kind, IReadOnlyList<DateOnly> days) => days.Min(d => Seats(d, kind));

    /// <summary>Mean hours of one shift of the kind.</summary>
    public double HoursOf(int kind) => _hours.GetValueOrDefault(kind);
}
