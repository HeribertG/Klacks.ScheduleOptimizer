// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Check for TeamFairnessRule. Always soft, whatever severity the record carries (a record with-expression
/// could still set Hard; the owner decision allows team fairness only as a soft term), so it never takes
/// part in the slot-incremental veto. Per window the metric is summed per agent of the scope, optionally
/// scaled to full time, and a spread above MaxSpread is reported once per window without an agent id.
/// </summary>
/// <param name="rule">The fairness rule</param>
/// <param name="context">Evaluation context</param>

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

internal sealed class TeamFairnessCheck : RuleCheck
{
    private const int MinimumParticipants = 2;

    private readonly FairnessMetric _metric;
    private readonly decimal _maxSpread;
    private readonly bool[] _isWeekendDay;
    private readonly decimal[] _scaleByAgent;
    private readonly bool[] _participates;
    private readonly int[] _windowFirstDay;
    private readonly int[] _windowLastDay;

    public TeamFairnessCheck(TeamFairnessRule rule, RuleEvaluationContext context)
        : base(rule, context)
    {
        _metric = rule.Metric;
        _maxSpread = rule.MaxSpread;
        _isWeekendDay = new bool[context.DayCount];
        for (var day = 0; day < context.DayCount; day++)
        {
            _isWeekendDay[day] = rule.WeekendDays.Contains(context.DateAt(day).DayOfWeek);
        }

        _scaleByAgent = new decimal[context.AgentCount];
        _participates = new bool[context.AgentCount];
        for (var agent = 0; agent < context.AgentCount; agent++)
        {
            var workload = context.Agents[agent].WorkloadPercent;
            _participates[agent] = AppliesTo(agent) && (!rule.ProRata || workload > 0m);
            _scaleByAgent[agent] = rule.ProRata && workload > 0m ? RuleTimeConstants.FullWorkloadPercent / workload : 1m;
        }

        (_windowFirstDay, _windowLastDay) = BuildWindows(rule.Window, context);
    }

    public override RuleSeverity Severity => RuleSeverity.Soft;

    public override bool IsPerAgent => false;

    public int WindowCount => _windowFirstDay.Length;

    public override void Evaluate(RulePlan plan, RuleBoundaryIndex boundary, RuleFindingCollector collector)
    {
        for (var window = 0; window < _windowFirstDay.Length; window++)
        {
            var participants = 0;
            var min = decimal.MaxValue;
            var max = decimal.MinValue;
            for (var agent = 0; agent < plan.AgentCount; agent++)
            {
                if (!_participates[agent])
                {
                    continue;
                }

                var value = WindowValue(plan, agent, window);
                participants++;
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }

            if (participants < MinimumParticipants)
            {
                continue;
            }

            var spread = max - min;
            if (spread > _maxSpread)
            {
                Report(collector, null, _windowFirstDay[window], spread, _maxSpread, spread - _maxSpread);
            }
        }
    }

    /// <summary>The compared value of one agent in one window: the metric sum, scaled to full time with ProRata.</summary>
    public decimal WindowValue(RulePlan plan, int agent, int window)
        => Sum(plan, agent, _windowFirstDay[window], _windowLastDay[window]) * _scaleByAgent[agent];

    /// <summary>
    /// Soft penalty from precomputed window values (one array per agent row, <paramref name="offset"/> = index of this
    /// check's first window in it); equal to Weight times the Excess Evaluate reports. Non-participating agents are skipped.
    /// </summary>
    public double PenaltyFromValues(IReadOnlyList<decimal[]> valuesByAgent, int offset)
    {
        var penalty = 0d;
        for (var window = 0; window < _windowFirstDay.Length; window++)
        {
            var participants = 0;
            var min = decimal.MaxValue;
            var max = decimal.MinValue;
            for (var agent = 0; agent < valuesByAgent.Count; agent++)
            {
                if (!_participates[agent])
                {
                    continue;
                }

                var value = valuesByAgent[agent][offset + window];
                participants++;
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }

            var spread = max - min;
            if (participants >= MinimumParticipants && spread > _maxSpread)
            {
                penalty += Rule.Weight * (double)(spread - _maxSpread);
            }
        }

        return penalty;
    }

    private int Sum(RulePlan plan, int agent, int firstDay, int lastDay)
    {
        var sum = 0;
        for (var day = firstDay; day <= lastDay; day++)
        {
            var cell = plan.Get(agent, day);
            if (!cell.IsWork)
            {
                continue;
            }

            sum += _metric switch
            {
                FairnessMetric.NightDays => cell.Has(RuleShiftKind.Night) ? 1 : 0,
                FairnessMetric.WeekendDays => _isWeekendDay[day] ? 1 : 0,
                _ => 1,
            };
        }

        return sum;
    }

    private static (int[] First, int[] Last) BuildWindows(FairnessWindow window, RuleEvaluationContext context)
    {
        if (window == FairnessWindow.PlanPeriod)
        {
            return ([0], [context.DayCount - 1]);
        }

        var slots = new CalendarSlots(context, RuleCalendar.ToCalendarPeriod(window));
        var first = new int[slots.Count];
        var last = new int[slots.Count];
        for (var slot = 0; slot < slots.Count; slot++)
        {
            first[slot] = slots.FirstDay(slot);
            last[slot] = slots.LastDay(slot);
        }

        return (first, last);
    }
}
