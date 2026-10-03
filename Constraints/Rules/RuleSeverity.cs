// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

/// <summary>Whether a planning rule vetoes a plan (Hard) or only adds a weighted penalty (Soft).</summary>
public enum RuleSeverity
{
    Hard,
    Soft,
}
