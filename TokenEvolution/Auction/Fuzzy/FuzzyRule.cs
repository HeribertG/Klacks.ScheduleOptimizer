// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution.Auction.Fuzzy;

/// <summary>
/// A single Mamdani rule. Antecedents are joined by AND (min) or OR (max). The consequent
/// names a term of the output variable; activation = aggregated antecedent degree.
/// </summary>
/// <param name="Name">Short identifier for explainability/logging</param>
/// <param name="Antecedents">List of (variable, term) pairs to evaluate</param>
/// <param name="Operator">"AND" (min) or "OR" (max)</param>
/// <param name="ConsequentVariable">Name of the output variable</param>
/// <param name="ConsequentTerm">Term to activate on the output</param>
public sealed record FuzzyRule(
    string Name,
    IReadOnlyList<RuleClause> Antecedents,
    string Operator,
    string ConsequentVariable,
    string ConsequentTerm);

/// <param name="Variable">Input variable name</param>
/// <param name="Term">Term name</param>
public sealed record RuleClause(string Variable, string Term);

/// <summary>
/// Result of activating one rule against a fuzzified input set.
/// </summary>
/// <param name="RuleName">Name of the rule</param>
/// <param name="ConsequentTerm">Output term that was activated</param>
/// <param name="Activation">Activation strength in [0,1]</param>
public sealed record RuleActivation(string RuleName, string ConsequentTerm, double Activation);
