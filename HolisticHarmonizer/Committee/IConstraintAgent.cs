// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Committee;

/// <summary>
/// A single deterministic constraint-checker that votes on whether a proposed swap is
/// acceptable from its perspective (e.g. hours balance, pause adequacy, rotation diversity).
/// Agents are stateless and pure — given the same bitmap and swap they always produce the
/// same verdict. The committee runs every agent for every step before <c>BatchEvaluator</c>
/// applies the swap, so per-step cost must be cheap (O(rowDays) at most).
/// </summary>
public interface IConstraintAgent
{
    /// <summary>Stable identifier surfaced in reject-memory entries. Must not be null or empty.</summary>
    string Name { get; }

    /// <summary>
    /// Evaluate the swap against the current bitmap state. The bitmap is read-only from the
    /// agent's perspective — implementations must NOT mutate cells. Look at the cells the swap
    /// would touch and decide whether it improves, harms, or is irrelevant to the agent's dimension.
    /// </summary>
    ConstraintAgentVerdict Evaluate(HarmonyBitmap before, PlanCellSwap swap);
}
