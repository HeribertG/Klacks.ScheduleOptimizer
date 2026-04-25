// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Initialization;

/// <summary>
/// Builds Locked-Token genome seeds from CoreLockedWork entries.
/// Groups consecutive work days per agent into Blocks, capped by maxConsecutiveDays.
/// The factory is shared by greedy and random population strategies.
/// </summary>
public static class LockedTokenFactory
{
    /// <summary>
    /// Converts the given locked works into <see cref="CoreToken"/> entries with IsLocked=true.
    /// Tokens are grouped per agent into blocks of consecutive calendar days (no gap),
    /// respecting <paramref name="maxConsecutiveDays"/> as an upper cap.
    /// </summary>
    /// <param name="lockedWorks">Existing DB works that must remain fixed</param>
    /// <param name="maxConsecutiveDays">Upper cap on consecutive days within a single block</param>
    public static IReadOnlyList<CoreToken> BuildLockedTokens(
        IReadOnlyList<CoreLockedWork> lockedWorks,
        int maxConsecutiveDays)
    {
        if (lockedWorks.Count == 0)
        {
            return [];
        }

        var result = new List<CoreToken>(lockedWorks.Count);
        var grouped = lockedWorks
            .GroupBy(w => w.AgentId)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var perAgent in grouped)
        {
            var sorted = perAgent
                .OrderBy(w => w.Date)
                .ThenBy(w => w.StartAt)
                .ToList();

            Guid currentBlock = Guid.NewGuid();
            int positionInBlock = 0;
            int distinctDayCount = 1;
            DateOnly lastDate = sorted[0].Date;

            foreach (var work in sorted)
            {
                var isFirst = ReferenceEquals(work, sorted[0]);
                if (!isFirst)
                {
                    if (work.Date == lastDate)
                    {
                        positionInBlock++;
                    }
                    else if (work.Date == lastDate.AddDays(1) && distinctDayCount < maxConsecutiveDays)
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

                    lastDate = work.Date;
                }

                result.Add(new CoreToken(
                    WorkIds: [work.WorkId],
                    ShiftTypeIndex: work.ShiftTypeIndex,
                    Date: work.Date,
                    TotalHours: work.TotalHours,
                    StartAt: work.StartAt,
                    EndAt: work.EndAt,
                    BlockId: currentBlock,
                    PositionInBlock: positionInBlock,
                    IsLocked: true,
                    LocationContext: work.LocationContext,
                    ShiftRefId: work.ShiftRefId,
                    AgentId: work.AgentId));
            }
        }

        return result;
    }
}
