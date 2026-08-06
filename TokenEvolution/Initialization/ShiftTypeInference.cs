// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// The one place that decides whether a shift is an early, a late or a night shift (index 0/1/2).
/// Every engine reads it from here - a second opinion anywhere else silently produces plans that
/// disagree about who may work what.
/// <para>
/// The bands are Early 06:00-14:59, Late 15:00-22:59, Night 23:00-05:59. A shift is classified by its
/// whole SPAN, not by its start alone, and the strongest band its span touches wins: Night beats Late
/// beats Early. So an 08:00-16:00 shift reaches into the late band and counts as late, and a
/// 05:00-13:00 shift starts inside the night band and counts as night, even though both spend most of
/// their time in the early band. The reason is the rule that matters to people: a shift that runs into
/// the night IS night work, whatever it says on the roster.
/// </para>
/// </summary>
public static class ShiftTypeInference
{
    /// <summary>Early shift.</summary>
    public const int EarlyIndex = 0;

    /// <summary>Late shift.</summary>
    public const int LateIndex = 1;

    /// <summary>Night shift.</summary>
    public const int NightIndex = 2;

    private const int MinutesPerDay = 24 * 60;
    private const int EarlyBandStart = 6 * 60;
    private const int LateBandStart = 15 * 60;
    private const int NightBandStart = 23 * 60;

    /// <summary>
    /// Classifies a shift by its span. The end is exclusive, so a shift ending exactly at 15:00 stays
    /// early - its last worked minute is 14:59. An end that is not after the start means the shift runs
    /// past midnight.
    /// </summary>
    /// <param name="start">Start time of day.</param>
    /// <param name="end">End time of day, exclusive.</param>
    public static int FromSpan(TimeOnly start, TimeOnly end)
    {
        var startMinute = ToMinute(start);
        var endMinute = ToMinute(end);

        if (startMinute == endMinute)
        {
            return FromStartTime(start);
        }

        // A span that ends at or before it starts wraps past midnight and becomes two intervals.
        var spans = endMinute > startMinute
            ? new[] { (startMinute, endMinute), (0, 0) }
            : [(startMinute, MinutesPerDay), (0, endMinute)];

        if (TouchesNight(spans))
        {
            return NightIndex;
        }

        return Touches(spans, LateBandStart, NightBandStart) ? LateIndex : EarlyIndex;
    }

    /// <summary>
    /// Classifies the start instant alone. Only for callers that genuinely have no end time; a caller
    /// that does must use <see cref="FromSpan"/>, otherwise the two disagree about the same shift.
    /// </summary>
    /// <param name="start">Start time of day.</param>
    public static int FromStartTime(TimeOnly start)
    {
        var minute = ToMinute(start);

        if (minute >= NightBandStart || minute < EarlyBandStart)
        {
            return NightIndex;
        }

        return minute < LateBandStart ? EarlyIndex : LateIndex;
    }

    /// <summary>Same as <see cref="FromSpan"/> for the string times a CoreShift carries.</summary>
    /// <param name="startTime">Start time, parseable as a time of day.</param>
    /// <param name="endTime">End time, parseable as a time of day.</param>
    public static int FromSpanString(string startTime, string endTime)
    {
        if (!TimeOnly.TryParse(startTime, out var start))
        {
            return EarlyIndex;
        }

        return TimeOnly.TryParse(endTime, out var end) ? FromSpan(start, end) : FromStartTime(start);
    }

    /// <summary>Classifies the start instant of a string time; prefer <see cref="FromSpanString"/>.</summary>
    /// <param name="startTime">Start time, parseable as a time of day.</param>
    public static int FromStartTimeString(string startTime)
        => TimeOnly.TryParse(startTime, out var time) ? FromStartTime(time) : EarlyIndex;

    private static bool TouchesNight((int Start, int End)[] spans)
        => Touches(spans, NightBandStart, MinutesPerDay) || Touches(spans, 0, EarlyBandStart);

    private static bool Touches((int Start, int End)[] spans, int bandStart, int bandEnd)
    {
        foreach (var (start, end) in spans)
        {
            if (start < end && start < bandEnd && bandStart < end)
            {
                return true;
            }
        }

        return false;
    }

    private static int ToMinute(TimeOnly time) => (time.Hour * 60) + time.Minute;
}
