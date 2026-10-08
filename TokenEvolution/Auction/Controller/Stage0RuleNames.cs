// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.TokenEvolution.Auction.Controller;

/// <summary>
/// Rule names a <see cref="Stage0HardConstraintChecker"/> verdict carries (VetoVerdict.RuleName). Keyword vetoes are
/// <see cref="KeywordPrefix"/> plus the keyword name, e.g. KeywordFree or KeywordOnlyEarly.
/// </summary>
public static class Stage0RuleNames
{
    public const string UnknownAgent = "UnknownAgent";
    public const string InvalidSlotDate = "InvalidSlotDate";
    public const string MaxConsecutiveDays = "MaxConsecutiveDays";
    public const string ContractDay = "ContractDay";
    public const string ContractWeekday = "ContractWeekday";
    public const string PerformsShiftWork = "PerformsShiftWork";
    public const string BreakBlocker = "BreakBlocker";
    public const string KeywordPrefix = "Keyword";
    public const string KeywordFree = KeywordPrefix + "Free";
    public const string BlacklistedShift = "BlacklistedShift";
    public const string MissingQualification = "MissingQualification";
    public const string MaximumHoursContractCap = "MaximumHoursContractCap";
    public const string MaxDailyHours = "MaxDailyHours";
    public const string OverlappingShift = "OverlappingShift";
    public const string ExistingWorkOverlap = "ExistingWorkOverlap";
    public const string MinPauseHours = "MinPauseHours";
    public const string RestrictedTimeWindow = "RestrictedTimeWindow";
    public const string WeeklyRestDays = "WeeklyRestDays";
}
