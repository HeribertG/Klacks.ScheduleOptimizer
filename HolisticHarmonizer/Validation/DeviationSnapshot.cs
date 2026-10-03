// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Validation;

/// <param name="Rows">Rows the snapshot covers (rows with a positive target only).</param>
/// <param name="AbsoluteSum">Sum of |worked - target| hours over the rows.</param>
/// <param name="SquaredSum">Sum of squared deviations over the rows.</param>
/// <param name="MaxRow">Largest single-row deviation among the rows.</param>
public sealed record DeviationSnapshot(int[] Rows, decimal AbsoluteSum, decimal SquaredSum, decimal MaxRow);
