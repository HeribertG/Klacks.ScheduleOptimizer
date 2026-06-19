// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Constraints;
using Klacks.ScheduleOptimizer.Models;

namespace Klacks.ScheduleOptimizer.Objective;

/// <summary>
/// The composite "Gesamtzustand" objective (v1). Engine-agnostic pure scorer: it consumes an
/// <see cref="ObjectiveInput"/> and returns a <see cref="ObjectiveResult"/> with a tiered feasibility
/// gate plus a weighted [0,1] scalar over the soft terms (S_Fehler, S_Stundenabgleich, S_Praeferenzen).
/// Posture (B-vs-C): soft penalties arbitrate trade-offs via the weights; the only hard protection is
/// the gate floors (mandatory-qual + legality) and — applied by the runner against the baseline — the
/// worst-agent hours floor, the per-agent blacklist cap and the churn gate-cap. No per-dimension
/// Pareto guards on soft dimensions (that is the frozen-optimizer trap). Deferred (not v1): continuity,
/// load-fairness beyond night/weekend, YTD carry-in, WARN/SOFT soft-errors, the weight learner.
/// </summary>
public sealed class CompositeObjective
{
    public ObjectiveResult Evaluate(ObjectiveInput input)
    {
        var gate = BuildGate(input.Violations);
        var fehler = ComputeFehler(input, gate);
        var (stundenabgleich, worstStunden) = ComputeStundenabgleich(input);
        var (praeferenzen, worstPraeferenzen, maxBlacklistFraction) = ComputePraeferenzen(input);

        var scalar = (ObjectiveConstants.WeightFehler * fehler)
            + (ObjectiveConstants.WeightStundenabgleich * stundenabgleich)
            + (ObjectiveConstants.WeightZufriedenheit * praeferenzen);

        return new ObjectiveResult(
            gate,
            scalar,
            new ObjectiveSubScores(fehler, stundenabgleich, praeferenzen),
            new ObjectiveDiagnostics(worstStunden, worstPraeferenzen, maxBlacklistFraction));
    }

    private static GateResult BuildGate(IReadOnlyList<ConstraintViolation> violations)
    {
        var qual = 0;
        var legality = 0;
        var under = 0;
        var over = 0;

        foreach (var v in violations)
        {
            switch (v.Kind)
            {
                case ViolationKind.QualificationMissing:
                    qual++;
                    break;
                case ViolationKind.UnderSupply:
                    under++;
                    break;
                case ViolationKind.OverSupply:
                    over++;
                    break;
                default:
                    // The nine legality kinds: MaxConsecutiveDays, MinPauseHours, MaxDailyHours,
                    // WorkOnDayViolation, PerformsShiftWorkViolation, PerDayKeywordViolation,
                    // BreakBlockerViolation, MaximumHoursExceeded, Overlap.
                    legality++;
                    break;
            }
        }

        return new GateResult(qual, legality, under, over);
    }

    /// <summary>
    /// Soft cleanliness term over supply violations: <c>exp(-(under+over)/D)</c> with demand basis
    /// <c>D = sum over shifts of max(1, RequiredAssignments)</c>. Strictly monotone — every extra
    /// supply violation lowers the score — and bounded to (0,1]. Demand-normalised so a twice-as-large
    /// plan with twice the violations scores comparably. The hard severity (mandatory-qual, legality)
    /// already lives in the gate, so this term only arbitrates soft coverage quality. Priority
    /// weighting of slots (CoreShift.Priority) is a deferred refinement.
    /// </summary>
    private static double ComputeFehler(ObjectiveInput input, GateResult gate)
    {
        var demand = 0.0;
        foreach (var shift in input.Context.Shifts)
        {
            // Same demand basis as the gate's CheckSlotSupply: only shifts that resolve to a real
            // (ShiftRefId, Date) slot count, so the normaliser cannot drift from the supply numerator.
            if (Guid.TryParse(shift.Id, out _) && DateOnly.TryParse(shift.Date, out _))
            {
                demand += Math.Max(1, shift.RequiredAssignments);
            }
        }

        if (demand <= 0)
        {
            return 1.0;
        }

        var supplyViolations = gate.UnderSupply + gate.OverSupply;
        return Math.Exp(-supplyViolations / demand);
    }

    /// <summary>
    /// Hours-alignment term. Canonical actual hours (TotalHours + break hours, no surcharges) compared
    /// to the effective target (v1 = raw GuaranteedHours; window-scaling and YTD carry-in deferred).
    /// Per-agent score <c>1 - min(1, dev/DEV_FULL)</c>, aggregated as an egalitarian mean (never
    /// rank-decayed). Zero-target agents are excluded from the reward so surplus cannot be free-parked
    /// on them; their overload is still caught by the gate's MaximumHoursExceeded floor. Returns the
    /// mean and the worst per-agent score (for the worst-agent no-regression guard).
    /// </summary>
    private static (double Mean, double Worst) ComputeStundenabgleich(ObjectiveInput input)
    {
        var workHours = CanonicalHours.WorkHoursByAgent(input.Assignments);
        var perAgent = new List<double>(input.Context.Agents.Count);

        foreach (var agent in input.Context.Agents)
        {
            var effTarget = agent.GuaranteedHours;
            if (effTarget <= ObjectiveConstants.ZeroTargetEpsilon)
            {
                continue;
            }

            var actual = GetOrZero(workHours, agent.Id) + GetOrZero(input.BreakHoursByAgent, agent.Id);
            var deviation = Math.Abs(actual - effTarget) / effTarget;
            var score = 1.0 - Math.Min(1.0, deviation / ObjectiveConstants.HoursDeviationFull);
            perAgent.Add(score);
        }

        if (perAgent.Count == 0)
        {
            return (1.0, 1.0);
        }

        return (perAgent.Average(), perAgent.Min());
    }

    /// <summary>
    /// Preference-satisfaction term. Per agent: a preferred-coverage axis (distinct preferred shift
    /// refs hit / preferred set size) and a blacklist-avoidance axis (1 - blacklisted-assignment
    /// fraction). The blacklist fraction is intrinsically PER-AGENT (the grill's fix against the
    /// plan-sum cheat), and concentration is surfaced by the worst-off blend. Agents with no
    /// preferences at all are excluded (no 0.5 dumping ground). Aggregated as
    /// <c>alpha*mean + (1-alpha)*min</c>. Returns the aggregate, the worst per-agent score and the
    /// maximum per-agent blacklist fraction (for the per-agent blacklist cap guard).
    /// </summary>
    private static (double Aggregate, double Worst, double MaxBlacklistFraction) ComputePraeferenzen(ObjectiveInput input)
    {
        var preferred = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
        var blacklist = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
        foreach (var p in input.Context.ShiftPreferences)
        {
            var target = p.Kind == ShiftPreferenceKind.Preferred ? preferred : blacklist;
            if (!target.TryGetValue(p.AgentId, out var set))
            {
                set = new HashSet<Guid>();
                target[p.AgentId] = set;
            }

            set.Add(p.ShiftRefId);
        }

        if (preferred.Count == 0 && blacklist.Count == 0)
        {
            return (1.0, 1.0, 0.0);
        }

        var assignmentsByAgent = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        foreach (var a in input.Assignments)
        {
            if (a.ShiftRefId == Guid.Empty)
            {
                continue;
            }

            if (!assignmentsByAgent.TryGetValue(a.AgentId, out var list))
            {
                list = [];
                assignmentsByAgent[a.AgentId] = list;
            }

            list.Add(a.ShiftRefId);
        }

        var agentsWithPreferences = new HashSet<string>(preferred.Keys, StringComparer.Ordinal);
        agentsWithPreferences.UnionWith(blacklist.Keys);

        var scores = new List<double>(agentsWithPreferences.Count);
        var maxBlacklistFraction = 0.0;

        foreach (var agentId in agentsWithPreferences)
        {
            var refs = assignmentsByAgent.TryGetValue(agentId, out var list) ? list : [];
            var workCount = refs.Count;

            double? preferredScore = null;
            if (preferred.TryGetValue(agentId, out var prefSet) && prefSet.Count > 0)
            {
                var distinctHits = refs.Distinct().Count(prefSet.Contains);
                preferredScore = Math.Min(1.0, (double)distinctHits / prefSet.Count);
            }

            double? blacklistScore = null;
            if (blacklist.TryGetValue(agentId, out var blackSet) && blackSet.Count > 0)
            {
                var blacklistHits = refs.Count(blackSet.Contains);
                var fraction = workCount > 0 ? (double)blacklistHits / workCount : 0.0;
                maxBlacklistFraction = Math.Max(maxBlacklistFraction, fraction);
                blacklistScore = 1.0 - Math.Min(1.0, fraction);
            }

            scores.Add(CombineAxes(preferredScore, blacklistScore));
        }

        if (scores.Count == 0)
        {
            return (1.0, 1.0, 0.0);
        }

        var mean = scores.Average();
        var worst = scores.Min();
        var aggregate = (ObjectiveConstants.WorstOffBlendAlpha * mean)
            + ((1.0 - ObjectiveConstants.WorstOffBlendAlpha) * worst);
        return (aggregate, worst, maxBlacklistFraction);
    }

    private static double CombineAxes(double? preferredScore, double? blacklistScore)
    {
        if (preferredScore.HasValue && blacklistScore.HasValue)
        {
            const double wp = ObjectiveConstants.PreferenceWeightPreferred;
            const double wb = ObjectiveConstants.PreferenceWeightBlacklist;
            return ((wp * preferredScore.Value) + (wb * blacklistScore.Value)) / (wp + wb);
        }

        return preferredScore ?? blacklistScore ?? 1.0;
    }

    private static double GetOrZero(IReadOnlyDictionary<string, double> map, string key)
        => map.TryGetValue(key, out var value) ? value : 0.0;
}
