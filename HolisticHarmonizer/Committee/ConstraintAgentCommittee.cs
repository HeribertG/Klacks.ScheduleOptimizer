// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Committee;

/// <summary>
/// Runs every <see cref="IConstraintAgent"/> over a proposed swap and aggregates the verdicts
/// into a single committee decision. Voting rule: a swap is blocked only when at least
/// <c>VetoCoalitionThreshold</c> agents object AND vetoes strictly outnumber approves. A lone
/// veto is treated as a hint, not a block — the score-greedy layer downstream still rejects
/// genuinely degrading swaps, and we do not want any single over-eager agent to starve the LLM
/// of approved moves. Ties and all-abstain results approve the swap so the LLM gets the
/// benefit of the doubt.
/// </summary>
/// <param name="agents">All committee members; order is preserved in the verdicts list.</param>
public sealed class ConstraintAgentCommittee
{
    /// <summary>Minimum number of veto votes required to block a swap. A single veto is downgraded to a hint.</summary>
    public const int VetoCoalitionThreshold = 2;

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

        var blocked = vetoes >= VetoCoalitionThreshold && vetoes > approves;
        var approved = !blocked;
        var summary = approved
            ? string.Empty
            : string.Join("; ", verdicts
                .Where(v => v.Vote == ConstraintAgentVote.Veto)
                .Select(v => $"{v.AgentName}: {v.Reason ?? "veto"}"));

        return new CommitteeDecision(approved, verdicts, summary);
    }
}
