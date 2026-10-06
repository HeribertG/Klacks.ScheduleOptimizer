// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

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
    /// Sends a small PNG containing a deterministic secret token and verifies the model can read
    /// the token back. Holistic Harmonizer / Wizard 3 mutate a bitmap representation, so any model
    /// that silently drops the attached image is unsuitable. A failed read is repeated with a fresh token
    /// before the model is reported as not vision-capable. Slower than <see cref="PingAsync"/>
    /// (up to 90 s in total, all reads included) but the only reliable filter for vision capability.
    /// </summary>
    Task<PlanProposalPingResult> CapabilityCheckAsync(string modelId, CancellationToken cancellationToken);

    Task<PlanProposalResponse> ProposeAsync(PlanProposalRequest request, CancellationToken cancellationToken);
}

/// <param name="IsHealthy">True only when the model returned a parseable JSON ping response.</param>
/// <param name="LatencyMs">Round-trip time in milliseconds.</param>
/// <param name="Error">Failure reason when <see cref="IsHealthy"/> is false; null on success.</param>
/// <param name="AnsweredButFailedImageCheck">
/// True only when the model did answer the vision capability check but did not read the image token back -
/// the one outcome that proves the model cannot read the schedule image. A timeout, an unavailable provider,
/// a rejected request or an exception leave it false: they say nothing about the model's vision capability.
/// </param>
public sealed record PlanProposalPingResult(bool IsHealthy, long LatencyMs, string? Error, bool AnsweredButFailedImageCheck = false);
