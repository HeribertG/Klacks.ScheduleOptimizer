// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.Common.Fuzzy;

/// <summary>
/// Mamdani fuzzy inference engine.
/// Steps: fuzzify crisp inputs → evaluate each rule (AND=min, OR=max) → aggregate per output term
/// (max across rules) → defuzzify to crisp via Centroid (Center-of-Gravity) over the output domain.
/// </summary>
public sealed class MamdaniInferenceEngine
{
    private readonly IReadOnlyDictionary<string, LinguisticVariable> _inputs;
    private readonly LinguisticVariable _output;
    private readonly IReadOnlyList<FuzzyRule> _rules;
    private readonly double _outputMin;
    private readonly double _outputMax;
    private readonly int _samples;

    public MamdaniInferenceEngine(
        IReadOnlyDictionary<string, LinguisticVariable> inputs,
        LinguisticVariable output,
        IReadOnlyList<FuzzyRule> rules,
        double outputMin = 0.0,
        double outputMax = 1.0,
        int samples = 100)
    {
        _inputs = inputs;
        _output = output;
        _rules = rules;
        _outputMin = outputMin;
        _outputMax = outputMax;
        _samples = samples;
    }

    /// <summary>
    /// Evaluate engine for a set of crisp inputs. Returns crisp output + list of fired rules
    /// (those with activation &gt; 0) for explainability.
    /// </summary>
    public InferenceResult Infer(IReadOnlyDictionary<string, double> crispInputs)
    {
        var activations = new List<RuleActivation>(_rules.Count);
        foreach (var rule in _rules)
        {
            var degrees = new List<double>(rule.Antecedents.Count);
            foreach (var clause in rule.Antecedents)
            {
                if (!_inputs.TryGetValue(clause.Variable, out var lv))
                {
                    degrees.Add(0);
                    continue;
                }
                if (!crispInputs.TryGetValue(clause.Variable, out var crisp))
                {
                    degrees.Add(0);
                    continue;
                }
                degrees.Add(lv.Mu(clause.Term, crisp));
            }

            var activation = degrees.Count == 0 ? 0
                : rule.Operator == "OR" ? degrees.Max()
                : degrees.Min();

            if (activation > 0)
            {
                activations.Add(new RuleActivation(rule.Name, rule.ConsequentTerm, activation));
            }
        }

        if (activations.Count == 0)
        {
            return new InferenceResult(0.0, []);
        }

        var crispOutput = Defuzzify(activations);
        return new InferenceResult(crispOutput, activations);
    }

    private double Defuzzify(IReadOnlyList<RuleActivation> activations)
    {
        var step = (_outputMax - _outputMin) / _samples;
        var num = 0.0;
        var den = 0.0;
        for (var i = 0; i <= _samples; i++)
        {
            var x = _outputMin + i * step;
            var aggregated = 0.0;
            foreach (var act in activations)
            {
                if (!_output.Terms.TryGetValue(act.ConsequentTerm, out var mf))
                {
                    continue;
                }
                var clipped = Math.Min(mf.Mu(x), act.Activation);
                if (clipped > aggregated)
                {
                    aggregated = clipped;
                }
            }
            num += x * aggregated;
            den += aggregated;
        }
        return den > 0 ? num / den : 0.0;
    }
}

/// <param name="CrispOutput">Defuzzified output value (e.g. 0..1 bid score)</param>
/// <param name="FiredRules">Rules with activation > 0</param>
public sealed record InferenceResult(double CrispOutput, IReadOnlyList<RuleActivation> FiredRules);
