// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Harmonizer.Scorer;

/// <param name="BlockSizeUniformity">[0,1] — 1 = all work blocks equal length, 0 = highly varied</param>
/// <param name="RestUniformity">[0,1] — 1 = all rest periods between blocks equal length, 0 = highly varied</param>
/// <param name="BlockHomogeneity">[0,1] — fraction of work blocks whose cells all share the same shift symbol</param>
/// <param name="TransitionCompliance">[0,1] — fraction of inter-block shift changes that respect the Early→Late→Night order</param>
/// <param name="ShiftTypeRotation">[0,1] — fairness of Early/Late/Night distribution across the agent's blocks; 1 = even rotation, 0 = single shift type only</param>
/// <param name="PreferredShiftFraction">[0,1] — fraction of work cells whose dominant symbol is in the agent's preferred-shift list; 1 = all preferred</param>
/// <param name="WorkBlockCount">Number of contiguous work blocks in the row (used for trivial-row detection)</param>
public sealed record RowFeatures(
    double BlockSizeUniformity,
    double RestUniformity,
    double BlockHomogeneity,
    double TransitionCompliance,
    double ShiftTypeRotation,
    double PreferredShiftFraction,
    int WorkBlockCount);
