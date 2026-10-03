// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// The effective contractual night window of an agent (the K2 surcharge night window resolved
/// SchedulingRule -> Contract -> settings -> default by the API). Both bounds are wall-clock times;
/// the window wraps midnight when End is not after Start (e.g. 23:00-06:00).
/// </summary>
/// <param name="Start">Inclusive start of the night window</param>
/// <param name="End">Exclusive end of the night window</param>

namespace Klacks.ScheduleOptimizer.Models;

public readonly record struct CoreNightWindow(TimeOnly Start, TimeOnly End);
