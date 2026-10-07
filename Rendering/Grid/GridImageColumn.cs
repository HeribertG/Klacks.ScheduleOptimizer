// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Rendering.Grid;

/// <summary>
/// Header of one column of a <see cref="GridImage"/>.
/// </summary>
/// <param name="TopLabel">First header line (e.g. the day number)</param>
/// <param name="BottomLabel">Second header line (e.g. the weekday letter)</param>
/// <param name="Tinted">True gives the whole column a light background tint (e.g. weekends)</param>
public sealed record GridImageColumn(string TopLabel, string BottomLabel, bool Tinted);
