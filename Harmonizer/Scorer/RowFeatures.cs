// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Scorer;

/// <param name="BlockSizeUniformity">[0,1] — 1 = all work blocks equal length, 0 = highly varied</param>
/// <param name="RestUniformity">[0,1] — 1 = all rest periods between blocks equal length, 0 = highly varied</param>
/// <param name="BlockHomogeneity">[0,1] — fraction of work blocks whose cells all share the same shift symbol</param>
/// <param name="TransitionCompliance">[0,1] — fraction of inter-block shift changes that respect the Early→Late→Night order</param>
/// <param name="WorkBlockCount">Number of contiguous work blocks in the row (used for trivial-row detection)</param>
public sealed record RowFeatures(
    double BlockSizeUniformity,
    double RestUniformity,
    double BlockHomogeneity,
    double TransitionCompliance,
    int WorkBlockCount);
