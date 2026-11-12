// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Validation;

/// <summary>The rows a batch touches and their shape before the batch.</summary>
/// <param name="Rows">Row indices, ascending; a lower index is a higher rank</param>
/// <param name="Shapes">Shape of each row, in the order of <paramref name="Rows"/></param>
public sealed record PlanProtectionSnapshot(int[] Rows, RowShape[] Shapes);
