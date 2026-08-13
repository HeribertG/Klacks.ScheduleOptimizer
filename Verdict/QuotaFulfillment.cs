// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Verdict;

/// <summary>
/// How far one agent fulfils one rest-window quota over the judged period. Fulfillment is the
/// fuzzy degree achieved/required capped at 1 — the number that decides how hard the quota lid
/// presses on the final score.
/// </summary>
/// <param name="AgentId">Agent the quota is owed to</param>
/// <param name="WindowHours">Size of the owed rest window in hours</param>
/// <param name="RequiredWindows">Windows owed for the period (period-scaled, fractional allowed)</param>
/// <param name="AchievedWindows">Windows the plan actually grants (long gaps credit multiples)</param>
/// <param name="Fulfillment">min(1, achieved / required)</param>
public sealed record QuotaFulfillment(
    string AgentId,
    double WindowHours,
    double RequiredWindows,
    double AchievedWindows,
    double Fulfillment);
