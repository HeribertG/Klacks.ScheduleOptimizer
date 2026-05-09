// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.ScheduleOptimizer.Harmonizer.Bitmap;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Llm;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Mutations;
using Klacks.ScheduleOptimizer.HolisticHarmonizer.Validation;

namespace Klacks.ScheduleOptimizer.HolisticHarmonizer.Candidates;

/// <summary>
/// Aggregates per-intent <see cref="IMoveCandidateGenerator"/> output, runs each candidate
/// through the hard-constraint <see cref="PlanMutationValidator"/>, deduplicates symmetric
/// pairs (rowA/rowB swappable, day equal), sorts by descending expected benefit, and trims
/// to the configured Top-K. Soft layers (committee + score-greedy) are intentionally NOT
/// applied — those still run when the LLM emits a candidate, so applying them here would
/// be redundant and would also discard valid tal-Schritt candidates the LLM may want to
/// combine with other moves.
/// </summary>
/// <param name="validator">Hard-constraint validator (locks, bounds, no-op, cross-day coverage).</param>
/// <param name="generators">One generator per intent. Generators not registered yield an empty list.</param>
/// <param name="topPerIntent">Per-intent cap on the candidate list size. Defaults to 15.</param>
public sealed class MoveCandidatePool
{
    public const int DefaultTopPerIntent = 15;

    private readonly PlanMutationValidator _validator;
    private readonly IReadOnlyDictionary<string, IMoveCandidateGenerator> _generators;
    private readonly int _topPerIntent;

    public MoveCandidatePool(
        PlanMutationValidator validator,
        IEnumerable<IMoveCandidateGenerator> generators,
        int topPerIntent = DefaultTopPerIntent)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(generators);
        if (topPerIntent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topPerIntent), "Top-K must be positive.");
        }

        _validator = validator;
        _generators = generators.ToDictionary(g => g.Intent, StringComparer.Ordinal);
        _topPerIntent = topPerIntent;
    }

    /// <summary>
    /// Returns the pre-validated, ranked, capped candidate list for the supplied intent. The
    /// list may be empty when no structural opportunity exists or when every raw suggestion
    /// fails hard validation.
    /// </summary>
    public IReadOnlyList<MoveCandidate> Generate(HarmonyBitmap bitmap, string intent)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);

        if (!_generators.TryGetValue(intent, out var generator))
        {
            return Array.Empty<MoveCandidate>();
        }

        var seen = new HashSet<CandidateKey>();
        var validated = new List<MoveCandidate>();

        foreach (var raw in generator.Generate(bitmap))
        {
            var key = CandidateKey.From(raw);
            if (!seen.Add(key))
            {
                continue;
            }
            var rejection = _validator.Validate(bitmap, ToSwap(raw));
            if (rejection is not null)
            {
                continue;
            }
            validated.Add(raw);
        }

        validated.Sort(static (a, b) => b.ExpectedBenefit.CompareTo(a.ExpectedBenefit));
        if (validated.Count > _topPerIntent)
        {
            validated.RemoveRange(_topPerIntent, validated.Count - _topPerIntent);
        }
        return validated;
    }

    private static PlanCellSwap ToSwap(MoveCandidate candidate)
        => new(candidate.RowA, candidate.DayA, candidate.RowB, candidate.DayB, Reason: string.Empty);

    private readonly record struct CandidateKey(int RowSmaller, int RowLarger, int DaySmaller, int DayLarger)
    {
        public static CandidateKey From(MoveCandidate candidate)
        {
            var rowSmaller = Math.Min(candidate.RowA, candidate.RowB);
            var rowLarger = Math.Max(candidate.RowA, candidate.RowB);
            var daySmaller = Math.Min(candidate.DayA, candidate.DayB);
            var dayLarger = Math.Max(candidate.DayA, candidate.DayB);
            return new CandidateKey(rowSmaller, rowLarger, daySmaller, dayLarger);
        }
    }
}
