// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.ScheduleOptimizer.Objective;

/// <param name="Fehler">Cleanliness sub-score in [0,1] (soft supply quality; higher is better)</param>
/// <param name="Stundenabgleich">Hours-alignment sub-score in [0,1], egalitarian mean over rewarded agents</param>
/// <param name="Praeferenzen">Preference-satisfaction sub-score in [0,1] (blacklist avoidance + preferred reward)</param>
public readonly record struct ObjectiveSubScores(
    double Fehler,
    double Stundenabgleich,
    double Praeferenzen);
