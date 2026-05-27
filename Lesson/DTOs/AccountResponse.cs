namespace Lesson.DTOs;

// -----------------------------------------------------------------------------
// C# NOTE: A separate response DTO decouples your API contract from the internal
// domain model. This is the same pattern as Spring's ResponseDTO or a custom
// Jackson-serialized view.
//
// Using a record here means the response is immutable once built — good practice
// for outbound data that should not be mutated after construction.
// -----------------------------------------------------------------------------

public record AccountResponse(
    Guid Id,
    string Owner,
    string Iban,
    decimal Balance,
    string Currency
);
