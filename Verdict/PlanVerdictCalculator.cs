// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using System.Globalization;
using Klacks.ScheduleOptimizer.Models;
using Klacks.ScheduleOptimizer.TokenEvolution.Diagnostics;
using Klacks.ScheduleOptimizer.TokenEvolution.Fitness;

namespace Klacks.ScheduleOptimizer.Verdict;

/// <summary>
/// Computes the final fuzzy verdict of one finished plan — purely observing, never steering: the
/// engine keeps enforcing every hard rule while plans are built, and this calculator only judges
/// finished candidates afterwards. Packages are the maximal runs of occupied calendar days the
/// short-package trace already defines (boundary works included, breaks excluded); rest gaps are
/// measured hour-exact from token times, boundary days without times count as full occupied days.
/// Day-to-day turnarounds inside a package are judged against the agent's daily rest
/// (MinRestHours); day pairs without token times are skipped there — no measuring base, no guess.
/// Soft terms reward structure; rest quotas and the legal minimum never add — they only cap.
/// A plan without any occupied day is vacuously clean: coverage is the engine's hard guarantee
/// and deliberately not part of the verdict.
/// </summary>
public static class PlanVerdictCalculator
{
    public const string CompactnessTermName = "compactness";

    public const string PurityTermName = "purity";

    public const string LengthDisciplineTermName = "lengthDiscipline";

    public const string KindFairnessTermName = "kindFairness";

    private const double HoursPerDay = 24;

    /// <summary>Judges the finished plan against the verdict configuration.</summary>
    /// <param name="scenario">Finished plan to judge</param>
    /// <param name="context">Wizard context holding period, agents and boundary works</param>
    /// <param name="config">Verdict settings; null uses the defaults</param>
    public static PlanVerdict Compute(
        CoreScenario scenario, CoreWizardContext context, PlanVerdictConfig? config = null)
    {
        var cfg = config ?? new PlanVerdictConfig();
        var periodDays = context.PeriodUntil.DayNumber - context.PeriodFrom.DayNumber + 1;
        var periodStart = context.PeriodFrom.ToDateTime(TimeOnly.MinValue);
        var periodEnd = context.PeriodUntil.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var timesByAgentDay = TimesByAgentDay(scenario);
        var agentsById = context.Agents.ToDictionary(a => a.Id, StringComparer.Ordinal);

        var findings = new List<VerdictFinding>();
        var quotas = new List<QuotaFulfillment>();
        var overlongCount = 0;

        foreach (var (agentId, dates) in ShortPackageTrace.DatesByAgent(scenario, context))
        {
            agentsById.TryGetValue(agentId, out var agent);
            var dayRuns = ShortPackageTrace.Runs(dates).ToList();
            var runs = dayRuns
                .Select(run => RunWindow(agentId, run, timesByAgentDay))
                .ToList();
            if (runs.Count == 0)
            {
                continue;
            }

            var maxWorkDays = agent?.MaxWorkDays ?? 0;
            if (maxWorkDays > 0)
            {
                overlongCount += runs.Count(r => r.LengthDays > maxWorkDays);
            }

            var requiredRestHours = (agent?.MinRestDays ?? 0) * HoursPerDay;
            var gaps = new List<double>
            {
                Math.Max(0, (runs[0].FirstStart - periodStart).TotalHours),
            };

            for (var i = 0; i + 1 < runs.Count; i++)
            {
                var gapHours = (runs[i + 1].FirstStart - runs[i].LastEnd).TotalHours;
                gaps.Add(Math.Max(0, gapHours));
                if (requiredRestHours > 0 && gapHours < requiredRestHours)
                {
                    var kind = gapHours < cfg.LegalMinimumRestHours
                        ? VerdictFindingKind.LegalMinimumBreach
                        : VerdictFindingKind.Scratch;
                    findings.Add(new VerdictFinding(
                        agentId, runs[i].LastEnd, runs[i + 1].FirstStart, gapHours, requiredRestHours, kind));
                }
            }

            gaps.Add(Math.Max(0, (periodEnd - runs[^1].LastEnd).TotalHours));

            var dailyRestHours = agent?.MinRestHours ?? 0;
            if (dailyRestHours > 0)
            {
                foreach (var dayRun in dayRuns)
                {
                    for (var day = dayRun.Start; day < dayRun.End; day = day.AddDays(1))
                    {
                        if (!timesByAgentDay.TryGetValue((agentId, day), out var earlier) ||
                            !timesByAgentDay.TryGetValue((agentId, day.AddDays(1)), out var later))
                        {
                            continue;
                        }

                        var turnaroundHours = (later.MinStart - earlier.MaxEnd).TotalHours;
                        if (turnaroundHours < dailyRestHours)
                        {
                            findings.Add(new VerdictFinding(
                                agentId, earlier.MaxEnd, later.MinStart, turnaroundHours,
                                dailyRestHours, VerdictFindingKind.DailyRestBreach));
                        }
                    }
                }
            }

            var quota = cfg.QuotaFor(requiredRestHours);
            if (quota is not null)
            {
                var required = quota.RequiredWindows(periodDays, cfg.ReferencePeriodDays);
                if (required > 0)
                {
                    var achieved = gaps.Sum(gap => Math.Floor(gap / quota.WindowHours));
                    quotas.Add(new QuotaFulfillment(
                        agentId, quota.WindowHours, required, achieved, Math.Min(1, achieved / required)));
                }
            }
        }

        var (shortCount, totalPackages) = ShortPackageTrace.Counts(scenario, context);
        var mixedCount = MixedKindPackageTrace.Count(scenario);
        var fairnessScore = TokenFitnessEvaluator.Create(context)
            .ComputeShiftKindFairnessScore(scenario, context);

        var terms = BuildTerms(cfg, shortCount, totalPackages, mixedCount, overlongCount, fairnessScore);
        var softScore = terms.Sum(t => t.Contribution);

        var scratchCount = findings.Count(f => f.Kind == VerdictFindingKind.Scratch);
        var scratchDeduction = Math.Min(cfg.ScratchPenaltyCeiling, scratchCount * cfg.ScratchPenalty);

        var minFulfillment = quotas.Count == 0 ? 1 : quotas.Min(q => q.Fulfillment);
        var quotaCap = cfg.QuotaShortfallCapFloor + ((1 - cfg.QuotaShortfallCapFloor) * minFulfillment);
        var hasLegalBreach = findings.Any(
            f => f.Kind is VerdictFindingKind.LegalMinimumBreach or VerdictFindingKind.DailyRestBreach);

        var score = Math.Min(softScore - scratchDeduction, quotaCap);
        if (hasLegalBreach)
        {
            score = Math.Min(score, cfg.LegalBreachCap);
        }

        score = Math.Clamp(score, 0, 1);

        var zone = hasLegalBreach ? VerdictZone.LegalMinimumBreach
            : minFulfillment < 1 ? VerdictZone.QuotaShortfall
            : scratchCount > 0 ? VerdictZone.Scratched
            : VerdictZone.Clean;

        return new PlanVerdict(
            score, zone, softScore, quotaCap, minFulfillment, terms, quotas, findings);
    }

    /// <summary>
    /// The explained soft ingredients. Compactness and length share the package total of the
    /// short-package trace; the mixed count comes from the kind trace, which reads boundary-free —
    /// the shared denominator slightly understates the mixed share and is accepted for stage one.
    /// </summary>
    private static List<VerdictTerm> BuildTerms(
        PlanVerdictConfig cfg,
        int shortCount,
        int totalPackages,
        int mixedCount,
        int overlongCount,
        double fairnessScore)
    {
        var weightSum =
            cfg.WeightCompactness + cfg.WeightPurity + cfg.WeightLengthDiscipline + cfg.WeightKindFairness;
        if (weightSum <= 0)
        {
            return [];
        }

        var compactness = totalPackages == 0
            ? 1
            : 1 - ((double)shortCount / totalPackages);
        var purity = totalPackages == 0
            ? 1
            : Math.Max(0, 1 - ((double)mixedCount / totalPackages));
        var lengthDiscipline = totalPackages == 0
            ? 1
            : Math.Max(0, 1 - ((double)overlongCount / totalPackages));
        var fairness = Math.Clamp(fairnessScore, 0, 1);

        return
        [
            Term(CompactnessTermName, cfg.WeightCompactness / weightSum, compactness,
                Explain("{0} of {1} packages are at most two days long", shortCount, totalPackages)),
            Term(PurityTermName, cfg.WeightPurity / weightSum, purity,
                Explain("{0} of {1} packages mix their shift kind", mixedCount, totalPackages)),
            Term(LengthDisciplineTermName, cfg.WeightLengthDiscipline / weightSum, lengthDiscipline,
                Explain("{0} of {1} packages exceed their agent's soft length", overlongCount, totalPackages)),
            Term(KindFairnessTermName, cfg.WeightKindFairness / weightSum, fairness,
                Explain("shift-kind fairness score of the fitness reads {0:0.###}", fairness)),
        ];
    }

    private static VerdictTerm Term(string name, double weight, double raw, string explanation)
        => new(name, weight, raw, weight * raw, explanation);

    private static string Explain(string format, params object[] args)
        => string.Format(CultureInfo.InvariantCulture, format, args);

    private static (DateTime FirstStart, DateTime LastEnd, int LengthDays) RunWindow(
        string agentId,
        (DateOnly Start, DateOnly End) run,
        IReadOnlyDictionary<(string AgentId, DateOnly Day), (DateTime MinStart, DateTime MaxEnd)> times)
    {
        var firstStart = DateTime.MaxValue;
        var lastEnd = DateTime.MinValue;
        for (var day = run.Start; day <= run.End; day = day.AddDays(1))
        {
            if (times.TryGetValue((agentId, day), out var dayTimes))
            {
                firstStart = dayTimes.MinStart < firstStart ? dayTimes.MinStart : firstStart;
                lastEnd = dayTimes.MaxEnd > lastEnd ? dayTimes.MaxEnd : lastEnd;
            }
            else
            {
                var dayStart = day.ToDateTime(TimeOnly.MinValue);
                var dayEnd = day.AddDays(1).ToDateTime(TimeOnly.MinValue);
                firstStart = dayStart < firstStart ? dayStart : firstStart;
                lastEnd = dayEnd > lastEnd ? dayEnd : lastEnd;
            }
        }

        return (firstStart, lastEnd, run.End.DayNumber - run.Start.DayNumber + 1);
    }

    private static Dictionary<(string AgentId, DateOnly Day), (DateTime MinStart, DateTime MaxEnd)>
        TimesByAgentDay(CoreScenario scenario)
    {
        var times = new Dictionary<(string AgentId, DateOnly Day), (DateTime MinStart, DateTime MaxEnd)>();
        foreach (var token in scenario.Tokens)
        {
            var key = (token.AgentId, token.Date);
            if (times.TryGetValue(key, out var existing))
            {
                times[key] = (
                    token.StartAt < existing.MinStart ? token.StartAt : existing.MinStart,
                    token.EndAt > existing.MaxEnd ? token.EndAt : existing.MaxEnd);
            }
            else
            {
                times[key] = (token.StartAt, token.EndAt);
            }
        }

        return times;
    }
}
