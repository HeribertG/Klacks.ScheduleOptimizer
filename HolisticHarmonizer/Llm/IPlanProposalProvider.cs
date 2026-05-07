// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Llm;

/// <summary>
/// Abstraction over the LLM call that turns a text-rendered schedule into a list of swap proposals.
/// The MVP implementation lives in Klacks.Api/Infrastructure and delegates to the existing
/// LLM service infrastructure; tests use an in-memory fake.
/// </summary>
public interface IPlanProposalProvider
{
    /// <summary>
    /// Sends a tiny pre-flight prompt to verify the model is reachable and can return JSON.
    /// Used by Holistic Harmonizer to fail fast (a few seconds) instead of waiting out the full HttpClient
    /// timeout when the configured model is offline, mis-keyed, or unable to follow the format.
    /// Trivial prompt — does not catch reasoning models that overrun their token budget on
    /// realistic loads. Use <see cref="CapabilityCheckAsync"/> for that.
    /// </summary>
    Task<PlanProposalPingResult> PingAsync(string modelId, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a realistic mini-schedule (3 agents × 5 days with an obvious swap candidate) and
    /// validates the response: parseable JSON, non-empty <c>swaps</c> array, valid coordinates.
    /// Slower than <see cref="PingAsync"/> (up to 90 s) but catches reasoning models that pass
    /// the trivial ping yet fail on full Holistic Harmonizer runs.
    /// </summary>
    Task<PlanProposalPingResult> CapabilityCheckAsync(string modelId, CancellationToken cancellationToken);

    Task<PlanProposalResponse> ProposeAsync(PlanProposalRequest request, CancellationToken cancellationToken);
}

/// <param name="IsHealthy">True only when the model returned a parseable JSON ping response.</param>
/// <param name="LatencyMs">Round-trip time in milliseconds.</param>
/// <param name="Error">Failure reason when <see cref="IsHealthy"/> is false; null on success.</param>
public sealed record PlanProposalPingResult(bool IsHealthy, long LatencyMs, string? Error);
