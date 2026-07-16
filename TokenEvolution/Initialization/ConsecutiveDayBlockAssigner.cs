// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Shared, pure helper that groups per-agent, date-sorted items into consecutive-day blocks and derives
/// each item's (BlockId, PositionInBlock). PositionInBlock is a running counter across the whole block
/// (same-day items increment it too); a following day opens a new block once the block already spans
/// maxConsecutiveDays distinct days. Used by both LockedTokenFactory and WarmStartTokenStrategy so the
/// block rules stay identical.
/// </summary>
public static class ConsecutiveDayBlockAssigner
{
    /// <summary>
    /// Assigns block ids and positions to <paramref name="items"/>, projecting each into a result token.
    /// </summary>
    /// <param name="items">Items to block; grouped by agent and sorted by date, then start time</param>
    /// <param name="agentId">Selector for the item's agent id (grouping key)</param>
    /// <param name="date">Selector for the item's calendar date</param>
    /// <param name="startAt">Selector for the item's start instant (secondary sort within a day)</param>
    /// <param name="maxConsecutiveDays">Upper cap on consecutive distinct days within a single block</param>
    /// <param name="project">Builds the result token from the item, its block id and its position in block</param>
    public static List<CoreToken> Assign<TItem>(
        IReadOnlyList<TItem> items,
        Func<TItem, string> agentId,
        Func<TItem, DateOnly> date,
        Func<TItem, DateTime> startAt,
        int maxConsecutiveDays,
        Func<TItem, Guid, int, CoreToken> project)
    {
        var result = new List<CoreToken>(items.Count);
        var grouped = items
            .GroupBy(agentId)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var perAgent in grouped)
        {
            var sorted = perAgent
                .OrderBy(date)
                .ThenBy(startAt)
                .ToList();

            Guid currentBlock = Guid.NewGuid();
            int positionInBlock = 0;
            int distinctDayCount = 1;
            DateOnly lastDate = date(sorted[0]);
            var isFirst = true;

            foreach (var item in sorted)
            {
                if (!isFirst)
                {
                    var itemDate = date(item);
                    if (itemDate == lastDate)
                    {
                        positionInBlock++;
                    }
                    else if (itemDate == lastDate.AddDays(1) && distinctDayCount < maxConsecutiveDays)
                    {
                        positionInBlock++;
                        distinctDayCount++;
                    }
                    else
                    {
                        currentBlock = Guid.NewGuid();
                        positionInBlock = 0;
                        distinctDayCount = 1;
                    }

                    lastDate = itemDate;
                }

                isFirst = false;
                result.Add(project(item, currentBlock, positionInBlock));
            }
        }

        return result;
    }
}
