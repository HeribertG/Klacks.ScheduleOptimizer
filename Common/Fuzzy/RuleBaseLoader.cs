// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Klacks.ScheduleOptimizer.Common.Fuzzy;

/// <summary>
/// Loads a fuzzy rule base from a JSON document. The JSON schema is a flat array of rule objects:
/// `[{ "name":..., "if":[{"var":..., "is":...}], "op":"AND|OR", "then":{"var":..., "is":...} }]`.
/// The default rule set is embedded as a resource at
/// `Klacks.ScheduleOptimizer.Common.Fuzzy.Resources.default-rules.json`.
/// </summary>
public static class RuleBaseLoader
{
    private const string DefaultResourceName =
        "Klacks.ScheduleOptimizer.Common.Fuzzy.Resources.default-rules.json";

    public static IReadOnlyList<FuzzyRule> LoadDefault()
    {
        var assembly = typeof(RuleBaseLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream(DefaultResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{DefaultResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static IReadOnlyList<FuzzyRule> Parse(string json)
    {
        var dtos = JsonSerializer.Deserialize<List<RuleDto>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidOperationException("Rule JSON parsed to null.");

        var rules = new List<FuzzyRule>(dtos.Count);
        foreach (var dto in dtos)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
            {
                throw new InvalidOperationException("Rule has empty name.");
            }
            if (dto.If is null || dto.If.Count == 0)
            {
                throw new InvalidOperationException($"Rule '{dto.Name}' has no antecedents.");
            }
            if (dto.Then is null)
            {
                throw new InvalidOperationException($"Rule '{dto.Name}' has no consequent.");
            }

            var antecedents = dto.If
                .Select(c => new RuleClause(c.Var ?? "", c.Is ?? ""))
                .ToList();

            rules.Add(new FuzzyRule(
                dto.Name,
                antecedents,
                string.IsNullOrWhiteSpace(dto.Op) ? "AND" : dto.Op,
                dto.Then.Var ?? "",
                dto.Then.Is ?? ""));
        }
        return rules;
    }

    private sealed class RuleDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("if")] public List<ClauseDto>? If { get; set; }
        [JsonPropertyName("op")] public string? Op { get; set; }
        [JsonPropertyName("then")] public ClauseDto? Then { get; set; }
    }

    private sealed class ClauseDto
    {
        [JsonPropertyName("var")] public string? Var { get; set; }
        [JsonPropertyName("is")] public string? Is { get; set; }
    }
}
