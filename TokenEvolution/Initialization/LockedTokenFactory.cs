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
        => ConsecutiveDayBlockAssigner.Assign(
            lockedWorks,
            w => w.AgentId,
            w => w.Date,
            w => w.StartAt,
            maxConsecutiveDays,
            (work, blockId, positionInBlock) => new CoreToken(
                WorkIds: [work.WorkId],
                ShiftTypeIndex: work.ShiftTypeIndex,
                Date: work.Date,
                TotalHours: work.TotalHours,
                StartAt: work.StartAt,
                EndAt: work.EndAt,
                BlockId: blockId,
                PositionInBlock: positionInBlock,
                IsLocked: true,
                LocationContext: work.LocationContext,
                ShiftRefId: work.ShiftRefId,
                AgentId: work.AgentId)
            {
                Surcharges = work.Surcharges,
            });
}
