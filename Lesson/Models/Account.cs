namespace Lesson.Models;

// -----------------------------------------------------------------------------
// C# NOTE: "record" is a reference type introduced in C# 9 that gives you
// value-based equality, a generated ToString(), and immutability by default.
//
// Java parallel:
//   Java 16+ record  →  C# record
//   Lombok @Value    →  C# record  (immutable)
//   Lombok @Data     →  C# record class (mutable) or class with properties
//
// Use records for DTOs and domain value objects where immutability is desired.
// For mutable entities (e.g. EF Core models) you will use plain classes (Lesson 03).
// -----------------------------------------------------------------------------

public record Account(
    Guid Id,
    string Owner,
    string Iban,
    decimal Balance,
    string Currency
);
