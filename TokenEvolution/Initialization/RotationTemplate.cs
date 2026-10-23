// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Canon template for the rotation start (Rotating Workforce Scheduling): one cyclic pattern of shift blocks
/// (early -> late -> night) and free days, given to every shift-working agent with an evenly spread offset, so the agents
/// work like voices of a canon - the same rhythm, shifted against each other.
/// <para>
/// The pattern is derived from the context, not configured: block length = the agents' typical soft block length
/// (MaxWorkDays, default 5, capped by MaxConsecutiveDays); the blocks of each shift kind are scaled by its share of the
/// period's demand; a night block is at most MaxConsecutiveDays - 1 days long and is otherwise split, because the morning
/// after a night counts as an occupied day; the free days follow from the work ratio (demand per day / shift-working
/// agents) and are spread over the gaps, never fewer than the agents' typical MinRestDays (at least 1).
/// </para>
/// <para>
/// The template is only a preference. It does not know availability, keywords, qualifications, absences or hours;
/// the strategy that uses it still checks every assignment with the same filter as every other seeding strategy.
/// </para>
/// </summary>
public sealed class RotationTemplate
{
    /// <summary>Template value of a free day.</summary>
    public const int FreeDay = -1;

    private const int DefaultBlockLength = 5;
    private const int MinBlockLength = 2;
    private const double MinWorkRatio = 0.1;
    private const double MaxWorkRatio = 0.95;

    private readonly int[] _pattern;
    private readonly Dictionary<string, int> _offsetByAgent;
    private readonly DateOnly _periodFrom;

    private RotationTemplate(int[] pattern, Dictionary<string, int> offsetByAgent, DateOnly periodFrom, double coverageFit)
    {
        _pattern = pattern;
        _offsetByAgent = offsetByAgent;
        _periodFrom = periodFrom;
        CoverageFit = coverageFit;
    }

    /// <summary>
    /// Share of the period's seats (date x shift kind) the canon staffs on its own, 0..1: per day and kind the number
    /// of agents the canon puts there, capped at the demand, summed and divided by the total demand. A canon that would
    /// leave many seats open or pile agents onto the same kind is no good start.
    /// </summary>
    public double CoverageFit { get; }

    /// <summary>The cyclic pattern: shift type index (0 early, 1 late, 2 night) or <see cref="FreeDay"/> per position.</summary>
    public IReadOnlyList<int> Pattern => _pattern;

    /// <summary>
    /// Expected shift type of the agent on the date (0/1/2), <see cref="FreeDay"/> for a planned free day, or null when
    /// the agent is not part of the canon (no shift work, no hours).
    /// </summary>
    public int? Expected(string agentId, DateOnly date)
    {
        if (!_offsetByAgent.TryGetValue(agentId, out var offset))
        {
            return null;
        }

        var position = (date.DayNumber - _periodFrom.DayNumber - offset) % _pattern.Length;
        return _pattern[position < 0 ? position + _pattern.Length : position];
    }

    /// <summary>Builds the template, or returns null when the context has no shift demand or fewer than two shift workers.</summary>
    public static RotationTemplate? Build(CoreWizardContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var days = context.PeriodUntil.DayNumber - context.PeriodFrom.DayNumber + 1;
        var workers = context.Agents
            .Where(a => a.PerformsShiftWork && (a.GuaranteedHours > 0 || a.FullTime > 0))
            .ToList();
        if (days <= 0 || workers.Count < 2)
        {
            return null;
        }

        var demand = new double[3];
        foreach (var shift in context.Shifts)
        {
            if (!TimeOnly.TryParse(shift.StartTime, out var start))
            {
                continue;
            }

            var end = TimeOnly.TryParse(shift.EndTime, out var parsedEnd) ? parsedEnd : start.AddHours(shift.Hours);
            demand[ShiftTypeInference.FromSpan(start, end)] += Math.Max(1, shift.RequiredAssignments);
        }

        var totalDemand = demand.Sum();
        if (totalDemand <= 0)
        {
            return null;
        }

        var pattern = BuildPattern(context, workers, demand, totalDemand, days);
        var anchor = CarryInContinuation.FirstPlannableDay(context);
        var history = CarryInContinuation.FixedShiftKinds(context);
        var packages = CarryInContinuation.Detect(context, anchor).ToDictionary(p => p.AgentId, StringComparer.Ordinal);
        var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < workers.Count; i++)
        {
            var spread = (int)((long)i * pattern.Length / workers.Count);
            offsets[workers[i].Id] = history.TryGetValue(workers[i].Id, out var kinds)
                ? AlignedOffset(pattern, context.PeriodFrom, anchor, kinds, packages.GetValueOrDefault(workers[i].Id), spread)
                : spread;
        }

        return new RotationTemplate(pattern, offsets, context.PeriodFrom, ComputeCoverageFit(context, pattern, offsets));
    }

    /// <summary>
    /// Offset that continues the agent's own history instead of cutting it: every offset is scored by how well the
    /// pattern reproduces the shift kinds (and free days) the agent actually started in the last cycle before the anchor,
    /// recent days weighing more. An agent entering the period in the middle of a night block is thus placed in the
    /// middle of the template's night block, one who just finished a block in its free gap. The future counts as well,
    /// and outweighs the history: an open carry-in package must go on with its kind for exactly its remaining days and
    /// then rest, a package finished on the last day before the anchor must be followed by rest. Without fixed work in
    /// that window, or on a tie, the evenly spread offset stays.
    /// </summary>
    private static int AlignedOffset(
        int[] pattern, DateOnly periodFrom, DateOnly anchor, IReadOnlyDictionary<DateOnly, int> kinds, CarryInPackage? package, int spread)
    {
        var length = pattern.Length;
        var windowStart = anchor.AddDays(-length);
        if (!kinds.Keys.Any(d => d >= windowStart && d < anchor))
        {
            return spread;
        }

        var best = spread;
        var bestScore = double.NegativeInfinity;
        var bestDistance = int.MaxValue;
        for (var offset = 0; offset < length; offset++)
        {
            var score = 0d;
            for (var distance = 1; distance <= length; distance++)
            {
                var day = anchor.AddDays(-distance);
                var actual = kinds.TryGetValue(day, out var kind) ? kind : FreeDay;
                var position = (day.DayNumber - periodFrom.DayNumber - offset) % length;
                if (pattern[position < 0 ? position + length : position] == actual)
                {
                    score += length - distance + 1;
                }
            }

            score += FutureScore(pattern, periodFrom, anchor, offset, kinds, package) * length * 2;

            var cyclicDistance = Math.Min(Math.Abs(offset - spread), length - Math.Abs(offset - spread));
            if (score > bestScore + 1e-9 || (Math.Abs(score - bestScore) <= 1e-9 && cyclicDistance < bestDistance))
            {
                best = offset;
                bestScore = score;
                bestDistance = cyclicDistance;
            }
        }

        return best;
    }

    /// <summary>
    /// How many of the days the carry-in dictates the offset honours: the remaining package days of the package's kind
    /// plus the rest day after them, or, when the agent worked the day before the anchor without an open package, the
    /// rest day on the anchor.
    /// </summary>
    private static int FutureScore(int[] pattern, DateOnly periodFrom, DateOnly anchor, int offset, IReadOnlyDictionary<DateOnly, int> kinds, CarryInPackage? package)
    {
        int At(DateOnly day)
        {
            var position = (day.DayNumber - periodFrom.DayNumber - offset) % pattern.Length;
            return pattern[position < 0 ? position + pattern.Length : position];
        }

        if (package is not null)
        {
            var hits = 0;
            for (var d = 0; d < package.RemainingDays; d++)
            {
                hits += At(anchor.AddDays(d)) == package.ShiftTypeIndex ? 1 : 0;
            }

            return hits + (At(anchor.AddDays(package.RemainingDays)) == FreeDay ? 1 : 0);
        }

        return kinds.ContainsKey(anchor.AddDays(-1)) && At(anchor) == FreeDay ? 1 : 0;
    }

    private static double ComputeCoverageFit(CoreWizardContext context, int[] pattern, Dictionary<string, int> offsets)
    {
        var demand = new Dictionary<(DateOnly Date, int Kind), int>();
        foreach (var shift in context.Shifts)
        {
            if (!DateOnly.TryParse(shift.Date, out var date) || !TimeOnly.TryParse(shift.StartTime, out var start))
            {
                continue;
            }

            var end = TimeOnly.TryParse(shift.EndTime, out var parsedEnd) ? parsedEnd : start.AddHours(shift.Hours);
            var key = (date, ShiftTypeInference.FromSpan(start, end));
            demand[key] = demand.GetValueOrDefault(key) + Math.Max(1, shift.RequiredAssignments);
        }

        var total = demand.Values.Sum();
        if (total == 0)
        {
            return 0;
        }

        var covered = 0;
        foreach (var ((date, kind), seats) in demand)
        {
            var staffed = 0;
            foreach (var offset in offsets.Values)
            {
                var position = (date.DayNumber - context.PeriodFrom.DayNumber - offset) % pattern.Length;
                staffed += pattern[position < 0 ? position + pattern.Length : position] == kind ? 1 : 0;
            }

            covered += Math.Min(staffed, seats);
        }

        return (double)covered / total;
    }

    private static int[] BuildPattern(CoreWizardContext context, List<CoreAgent> workers, double[] demand, double totalDemand, int days)
    {
        var maxConsecutive = Math.Max(MinBlockLength, context.SchedulingMaxConsecutiveDays);
        var blockLength = Math.Clamp(Median(workers.Select(a => a.MaxWorkDays).Where(v => v > 0), DefaultBlockLength), MinBlockLength, maxConsecutive);
        var minGap = Math.Max(1, Median(workers.Select(a => a.MinRestDays).Where(v => v > 0), 1));
        var workRatio = Math.Clamp(totalDemand / days / workers.Count, MinWorkRatio, MaxWorkRatio);
        var activeKinds = Enumerable.Range(0, 3).Count(k => demand[k] > 0);

        var blocks = new List<(int Kind, int Length)>();
        for (var kind = 0; kind < 3; kind++)
        {
            if (demand[kind] <= 0)
            {
                continue;
            }

            var length = Math.Max(1, (int)Math.Round(blockLength * activeKinds * demand[kind] / totalDemand));
            var cap = kind == ShiftTypeInference.NightIndex ? Math.Max(1, maxConsecutive - 1) : maxConsecutive;
            while (length > cap)
            {
                var part = Math.Min(cap, (length + 1) / 2);
                blocks.Add((kind, part));
                length -= part;
            }

            blocks.Add((kind, length));
        }

        var work = blocks.Sum(b => b.Length);
        var cycle = Math.Max(work + blocks.Count * minGap, (int)Math.Round(work / workRatio));
        var free = cycle - work;
        var pattern = new List<int>(cycle);
        for (var i = 0; i < blocks.Count; i++)
        {
            pattern.AddRange(Enumerable.Repeat(blocks[i].Kind, blocks[i].Length));
            var remainingGaps = blocks.Count - i;
            var gap = Math.Max(minGap, (int)Math.Round((double)free / remainingGaps));
            gap = Math.Min(gap, free - (remainingGaps - 1) * minGap);
            pattern.AddRange(Enumerable.Repeat(FreeDay, Math.Max(minGap, gap)));
            free -= Math.Max(minGap, gap);
        }

        return pattern.ToArray();
    }

    private static int Median(IEnumerable<int> values, int fallback)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted.Length == 0 ? fallback : sorted[sorted.Length / 2];
    }
}
