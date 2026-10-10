// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

internal sealed record SharedDayRange(List<CoreToken> FromA, List<CoreToken> FromB, bool CoversBothBlocks);
