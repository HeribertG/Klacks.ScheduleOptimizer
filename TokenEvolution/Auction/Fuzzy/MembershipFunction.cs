// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.ScheduleOptimizer.TokenEvolution.Auction.Fuzzy;

/// <summary>
/// Membership function for a fuzzy linguistic term. Computes the degree of membership mu in [0,1]
/// for a given crisp value. Trapezoid and Triangular implementations are provided.
/// </summary>
public abstract class MembershipFunction
{
    public abstract double Mu(double crisp);
}

/// <summary>Trapezoidal MF: rises from a→b, plateau b→c, falls c→d. b≤c required.</summary>
public sealed class TrapezoidMf : MembershipFunction
{
    public double A { get; }
    public double B { get; }
    public double C { get; }
    public double D { get; }

    public TrapezoidMf(double a, double b, double c, double d)
    {
        if (!(a <= b && b <= c && c <= d))
        {
            throw new ArgumentException($"Trapezoid requires a<=b<=c<=d, got ({a},{b},{c},{d})");
        }
        A = a; B = b; C = c; D = d;
    }

    public override double Mu(double x)
    {
        if (x <= A || x >= D) return 0;
        if (x >= B && x <= C) return 1;
        if (x < B) return (x - A) / (B - A);
        return (D - x) / (D - C);
    }
}

/// <summary>Triangular MF: rises a→b, falls b→c.</summary>
public sealed class TriangularMf : MembershipFunction
{
    public double A { get; }
    public double B { get; }
    public double C { get; }

    public TriangularMf(double a, double b, double c)
    {
        if (!(a <= b && b <= c))
        {
            throw new ArgumentException($"Triangle requires a<=b<=c, got ({a},{b},{c})");
        }
        A = a; B = b; C = c;
    }

    public override double Mu(double x)
    {
        if (x <= A || x >= C) return 0;
        if (x == B) return 1;
        if (x < B) return (x - A) / (B - A);
        return (C - x) / (C - B);
    }
}
