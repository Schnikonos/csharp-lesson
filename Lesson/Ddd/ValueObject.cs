namespace Lesson.Ddd;

/// <summary>
/// Lesson 19-A — Value Object base class.
///
/// A value object is defined entirely by its properties (no identity).
/// Two value objects with the same properties are equal.
/// Value objects are immutable.
///
/// C# record struct is the ideal implementation: compiler-generated
/// structural equality + immutability by default.
///
/// Java parallel:
///   A final class with @Embeddable, overriding equals()/hashCode() by all fields
///   Vavr Value&lt;T&gt; / Lombok @Value
/// </summary>
/// <example>
/// // Define a value object
/// public readonly record struct Money(decimal Amount, string Currency)
/// {
///     public static Money Zero(string currency = "USD") => new(0, currency);
///     public Money Add(Money other)
///     {
///         if (Currency != other.Currency)
///             throw new DomainException("Cannot add different currencies");
///         return new(Amount + other.Amount, Currency);
///     }
/// }
/// </example>
public abstract class ValueObject
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public override bool Equals(object? obj)
    {
        if (obj is null || obj.GetType() != GetType()) return false;
        return GetEqualityComponents()
            .SequenceEqual(((ValueObject)obj).GetEqualityComponents());
    }

    public override int GetHashCode() =>
        GetEqualityComponents()
            .Aggregate(0, (hash, c) => HashCode.Combine(hash, c?.GetHashCode() ?? 0));

    public static bool operator ==(ValueObject? a, ValueObject? b) =>
        a is null ? b is null : a.Equals(b);

    public static bool operator !=(ValueObject? a, ValueObject? b) => !(a == b);
}
