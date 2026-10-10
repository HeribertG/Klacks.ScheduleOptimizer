// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Common.Rotation;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.TokenEvolution.Operators;

internal sealed record RotationSwapBase(
    CoreScenario Scenario,
    double BlockOrder,
    int MixedPackages,
    Dictionary<string, RotationAssessment> RotationByAgent);
