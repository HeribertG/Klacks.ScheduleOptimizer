// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Harmonizer.Conductor;

/// <summary>
/// Validates whether a replace move is admissible. Implementations layer constraint sources;
/// a Phase-4 implementation only enforces bitmap-local invariants (locks, indices), while a
/// Phase-6 implementation will additionally tokenise the affected region and run the full
/// Stage0/Stage1 constraint checkers from Wizard 1.
/// </summary>
public interface IReplaceValidator
{
    bool IsValid(HarmonyBitmap bitmap, ReplaceMove move);
}
