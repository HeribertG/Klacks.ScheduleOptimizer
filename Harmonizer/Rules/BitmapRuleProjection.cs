// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Constraints.Rules;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Harmonizer.Rules;

/// <summary>
/// Projects bitmap cells onto rule days with the exact per-work segments of RulePlanFactory.FromBitmapInput: a
/// cell's WorkIds lead back to the input assignments, so a merged cell still counts each of its works (night
/// segments, overlong shifts) and the classification follows the agent of the row the cell currently sits in.
/// Swaps move cells by reference, so the segments are resolved once per cell and the rule day once per (cell,
/// agent). A worked cell whose works are unknown (synthetic cells) falls back to the merged cell span; ignored
/// works (container sub-works) never count. Not thread-safe; one instance per run.
/// </summary>
/// <param name="input">Bitmap input whose assignments the cells were built from</param>
/// <param name="context">Rule context (agent rows, period = bitmap days)</param>
/// <param name="ignoredWorkIds">Works the rules never count</param>
public sealed class BitmapRuleProjection
{
    private const int NotInContext = -1;

    private readonly RuleEvaluationContext _context;
    private readonly IReadOnlySet<Guid> _ignoredWorkIds;
    private readonly Dictionary<Guid, int> _segmentIndexByWorkId = new();
    private readonly List<RuleSegment> _segments = new();
    private readonly Dictionary<Cell, RuleSegment[]> _segmentsByCell = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Cell, RuleDay?[]> _daysByCell = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BitmapAgent, int> _agentIndex = new(ReferenceEqualityComparer.Instance);

    public BitmapRuleProjection(BitmapInput input, RuleEvaluationContext context, IReadOnlySet<Guid> ignoredWorkIds)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(ignoredWorkIds);
        _context = context;
        _ignoredWorkIds = ignoredWorkIds;
        foreach (var assignment in input.Assignments)
        {
            if (!RuleSegmentMapper.IsWorked(assignment.Symbol) || AllIgnored(assignment.WorkIds))
            {
                continue;
            }

            var index = _segments.Count;
            _segments.Add(RuleSegmentMapper.FromBitmapAssignment(assignment));
            foreach (var workId in assignment.WorkIds)
            {
                _segmentIndexByWorkId.TryAdd(workId, index);
            }
        }
    }

    public RuleEvaluationContext Context => _context;

    /// <summary>Context row of the agent, or -1 when the agent is not part of the rule context.</summary>
    public int AgentIndexOf(BitmapAgent agent)
    {
        if (_agentIndex.TryGetValue(agent, out var index))
        {
            return index;
        }

        index = _context.TryGetAgentIndex(agent.Id, out var found) ? found : NotInContext;
        _agentIndex[agent] = index;
        return index;
    }

    /// <summary>The rule day the cell yields when it sits in a row of the given agent.</summary>
    public RuleDay DayOf(Cell cell, int agentIndex)
    {
        if (cell.Symbol == CellSymbol.Free && cell.WorkIds.Count == 0)
        {
            return RuleDay.Free;
        }

        if (!_daysByCell.TryGetValue(cell, out var days))
        {
            days = new RuleDay?[_context.AgentCount];
            _daysByCell[cell] = days;
        }

        if (days[agentIndex] is { } cached)
        {
            return cached;
        }

        var agent = _context.Agents[agentIndex];
        var day = RuleDay.Free;
        foreach (var segment in SegmentsOf(cell))
        {
            day = day.WithSegment(segment, agent);
        }

        days[agentIndex] = day;
        return day;
    }

    /// <summary>Writes the row's cells into the plan row of the agent (bitmap days = context days).</summary>
    public void ProjectRow(HarmonyBitmap bitmap, int row, int agentIndex, RulePlan plan)
    {
        for (var day = 0; day < bitmap.DayCount; day++)
        {
            plan.Set(agentIndex, day, DayOf(bitmap.GetCell(row, day), agentIndex));
        }
    }

    /// <summary>A fresh plan holding every row of the bitmap whose agent is part of the context.</summary>
    public RulePlan Project(HarmonyBitmap bitmap)
    {
        var plan = new RulePlan(_context);
        for (var row = 0; row < bitmap.RowCount; row++)
        {
            var agentIndex = AgentIndexOf(bitmap.Rows[row]);
            if (agentIndex != NotInContext)
            {
                ProjectRow(bitmap, row, agentIndex, plan);
            }
        }

        return plan;
    }

    private RuleSegment[] SegmentsOf(Cell cell)
    {
        if (_segmentsByCell.TryGetValue(cell, out var cached))
        {
            return cached;
        }

        var indexes = new List<int>(cell.WorkIds.Count);
        var hasUnknownWork = false;
        foreach (var workId in cell.WorkIds)
        {
            if (_ignoredWorkIds.Contains(workId))
            {
                continue;
            }

            if (!_segmentIndexByWorkId.TryGetValue(workId, out var index))
            {
                hasUnknownWork = true;
                continue;
            }

            if (!indexes.Contains(index))
            {
                indexes.Add(index);
            }
        }

        RuleSegment[] segments;
        if (indexes.Count > 0)
        {
            segments = indexes.Select(index => _segments[index]).ToArray();
        }
        else if (RuleSegmentMapper.IsWorked(cell.Symbol) && (hasUnknownWork || cell.WorkIds.Count == 0))
        {
            segments = [RuleSegmentMapper.FromCell(string.Empty, default, cell)];
        }
        else
        {
            segments = [];
        }

        _segmentsByCell[cell] = segments;
        return segments;
    }

    private bool AllIgnored(IReadOnlyList<Guid> workIds)
    {
        if (_ignoredWorkIds.Count == 0 || workIds.Count == 0)
        {
            return false;
        }

        foreach (var workId in workIds)
        {
            if (!_ignoredWorkIds.Contains(workId))
            {
                return false;
            }
        }

        return true;
    }
}
