// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

internal sealed record KindBlock(int Kind, DateOnly FirstDay, DateOnly LastDay, List<CoreToken> Tokens);
