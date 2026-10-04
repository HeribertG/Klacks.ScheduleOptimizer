// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.ScheduleOptimizer.Constraints.Rules;
using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;

namespace Klacks.ScheduleOptimizer.Harmonizer.Rules;

/// <summary>
/// Which (agent, shift symbol) runs a MaxConsecutiveOfKind planning rule already governs. The rotation agent of the
/// stage-3 committee abstains there, so its fixed "three in a row" heuristic does not overrule a configured run
/// length in either direction. Kind Work governs every worked symbol; Early, Late and Night their own symbol (rule
/// night is the overlap with the night window, the cell symbol its shift-type proxy).
/// </summary>
/// <param name="rules">Rule set of the run</param>
public sealed class PlanningRuleRunGovernance
{
    private static readonly CellSymbol[] WorkedSymbols = [CellSymbol.Early, CellSymbol.Late, CellSymbol.Night, CellSymbol.Other];

    private readonly HashSet<CellSymbol> _everyAgent = new();
    private readonly Dictionary<string, HashSet<CellSymbol>> _byAgent = new(StringComparer.Ordinal);

    public PlanningRuleRunGovernance(IReadOnlyList<PlanRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        foreach (var rule in rules.OfType<MaxConsecutiveOfKindRule>())
        {
            var symbols = SymbolsOf(rule.ShiftKind);
            if (rule.AgentScope is null)
            {
                _everyAgent.UnionWith(symbols);
                continue;
            }

            foreach (var agentId in rule.AgentScope)
            {
                if (!_byAgent.TryGetValue(agentId, out var set))
                {
                    set = new HashSet<CellSymbol>();
                    _byAgent[agentId] = set;
                }

                set.UnionWith(symbols);
            }
        }
    }

    public bool IsEmpty => _everyAgent.Count == 0 && _byAgent.Count == 0;

    public bool Governs(string agentId, CellSymbol symbol)
        => _everyAgent.Contains(symbol) || (_byAgent.TryGetValue(agentId, out var set) && set.Contains(symbol));

    private static IEnumerable<CellSymbol> SymbolsOf(RuleShiftKind kind) => kind switch
    {
        RuleShiftKind.Work => WorkedSymbols,
        RuleShiftKind.Early => [CellSymbol.Early],
        RuleShiftKind.Late => [CellSymbol.Late],
        RuleShiftKind.Night => [CellSymbol.Night],
        _ => [],
    };
}
