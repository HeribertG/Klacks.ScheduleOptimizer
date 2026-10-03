// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Constraints.Rules;

/// <summary>
/// Day classification a sequence rule matches on. Work = any worked segment; Early/Late follow the shift-type
/// index; Night is the overlap with the agent night window (shift-type fallback only without a window).
/// </summary>
public enum RuleShiftKind
{
    Work,
    Early,
    Late,
    Night,
}
