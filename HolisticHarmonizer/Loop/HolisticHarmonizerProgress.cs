// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Loop;

/// <summary>
/// Per-iteration progress snapshot emitted by <c>HolisticHarmonizerEngine</c>. Decoupled from any
/// transport layer — consumers (e.g. SignalR runner) translate this into wire DTOs.
/// </summary>
/// <param name="IterationIndex">Zero-based index of the just-completed inner-loop iteration.</param>
/// <param name="MaxIterations">Configured upper bound for the inner loop.</param>
/// <param name="BestFitness">Best fitness observed since run start, in [0,1].</param>
/// <param name="AcceptedBatchCount">Total accepted/partially accepted batches so far.</param>
/// <param name="RejectedBatchCount">Total rejected/would-degrade batches so far.</param>
/// <param name="ElapsedMs">Wall-clock time since run start in milliseconds.</param>
public sealed record HolisticHarmonizerProgress(
    int IterationIndex,
    int MaxIterations,
    double BestFitness,
    int AcceptedBatchCount,
    int RejectedBatchCount,
    long ElapsedMs);
