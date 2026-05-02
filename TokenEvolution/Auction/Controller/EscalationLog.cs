// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution.Auction.Controller;

/// <summary>
/// Collects Stage-1 relaxations that the controller had to allow because no Stage-0-and-Stage-1-clean
/// candidate was available. Used for telemetry and UI display.
/// </summary>
public sealed class EscalationLog
{
    private readonly List<EscalationEntry> _entries = [];

    public IReadOnlyList<EscalationEntry> Entries => _entries;

    public void Record(string agentId, DateOnly date, VetoVerdict verdict)
    {
        _entries.Add(new EscalationEntry(agentId, date, verdict.RuleName, verdict.Hint));
    }
}

/// <param name="AgentId">Agent that had its Stage-1 violation accepted</param>
/// <param name="Date">Date of the slot</param>
/// <param name="RuleName">Soft rule that was relaxed</param>
/// <param name="Hint">Human-readable reason</param>
public sealed record EscalationEntry(string AgentId, DateOnly Date, string RuleName, string Hint);
