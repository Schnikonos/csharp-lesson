using Lesson.Ddd;

namespace Lesson.Domain;

/// <summary>
/// Lesson 19-A — Money value object using C# record struct.
///
/// record struct gives us:
///   - Immutability (init-only setters)
///   - Structural equality (compiler-generated Equals / GetHashCode)
///   - Positional constructor
///
/// This is simpler than the ValueObject base class for small, leaf-level values.
///
/// Java parallel:
///   @Embeddable final class Money overriding equals()/hashCode()
///   Vavr Money / JSR-354 MonetaryAmount
/// </summary>
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency = "USD") => new(0, currency);

    public Money Add(Money other)
    {
        if (Currency != other.Currency)
            throw new DomainException($"Cannot add {Currency} to {other.Currency}");
        return new(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        if (Currency != other.Currency)
            throw new DomainException($"Cannot subtract {other.Currency} from {Currency}");
        if (Amount < other.Amount)
            throw new DomainException("Insufficient funds");
        return new(Amount - other.Amount, Currency);
    }

    public override string ToString() => $"{Amount:F2} {Currency}";
}

/// <summary>
/// Domain exceptions — typed errors that represent business-rule violations.
/// They are caught by the global exception handler and mapped to 422 Unprocessable Entity.
///
/// Java parallel: custom RuntimeException hierarchy; @ResponseStatus(422)
/// </summary>
public class DomainException(string message) : Exception(message);
