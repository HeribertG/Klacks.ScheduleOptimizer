// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

/// <summary>
/// A swap proposal that was discarded along with the structured reason. Returned to the UI so
/// the operator can inspect why the LLM's idea was rejected.
/// </summary>
/// <param name="Swap">The swap proposal that failed validation.</param>
/// <param name="Reason">Structured rejection cause for filtering and UI display.</param>
/// <param name="Detail">Free-text detail (e.g. which cell was locked).</param>
public sealed record PlanMutationRejection(
    PlanCellSwap Swap,
    PlanMutationRejectionReason Reason,
    string Detail);
