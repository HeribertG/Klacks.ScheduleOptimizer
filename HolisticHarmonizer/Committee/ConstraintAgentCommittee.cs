// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Committee;

/// <summary>
/// Runs every <see cref="IConstraintAgent"/> over a proposed swap and aggregates the verdicts
/// into a single committee decision. Voting rule: <c>Approved = vetoes &lt;= approves</c> — i.e.
/// the swap is blocked only when strictly more agents object than support it. Ties and
/// all-abstain results approve the swap so the LLM gets the benefit of the doubt; the
/// score-greedy layer downstream still has the final word.
/// </summary>
/// <param name="agents">All committee members; order is preserved in the verdicts list.</param>
public sealed class ConstraintAgentCommittee
{
    private readonly IReadOnlyList<IConstraintAgent> _agents;

    public ConstraintAgentCommittee(IEnumerable<IConstraintAgent> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);
        _agents = agents.ToArray();
    }

    public CommitteeDecision Evaluate(HarmonyBitmap before, PlanCellSwap swap)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(swap);

        var verdicts = new ConstraintAgentVerdict[_agents.Count];
        var approves = 0;
        var vetoes = 0;
        for (var i = 0; i < _agents.Count; i++)
        {
            var verdict = _agents[i].Evaluate(before, swap);
            verdicts[i] = verdict;
            if (verdict.Vote == ConstraintAgentVote.Approve) approves++;
            else if (verdict.Vote == ConstraintAgentVote.Veto) vetoes++;
        }

        var approved = vetoes <= approves;
        var summary = approved
            ? string.Empty
            : string.Join("; ", verdicts
                .Where(v => v.Vote == ConstraintAgentVote.Veto)
                .Select(v => $"{v.AgentName}: {v.Reason ?? "veto"}"));

        return new CommitteeDecision(approved, verdicts, summary);
    }
}
