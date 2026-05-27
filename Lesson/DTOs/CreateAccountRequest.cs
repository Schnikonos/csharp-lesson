using System.ComponentModel.DataAnnotations;

namespace Lesson.DTOs;

// -----------------------------------------------------------------------------
// C# NOTE: DTOs (Data Transfer Objects) are plain classes or records used to
// define the shape of the API request/response — separate from the domain model.
//
// Data Annotations (System.ComponentModel.DataAnnotations) provide declarative
// validation that ASP.NET Core checks automatically before your action runs.
//
// Java parallel:
//   @NotNull / @NotBlank  →  [Required]
//   @Size(min, max)       →  [StringLength(max, MinimumLength = min)]
//   @Min / @Max           →  [Range(min, max)]
//   @Valid on parameter   →  automatic in [ApiController] — no annotation needed
// -----------------------------------------------------------------------------

public record CreateAccountRequest(
    [Required(ErrorMessage = "Owner name is required.")]
    [StringLength(100, MinimumLength = 2)]
    string Owner,

    [Required(ErrorMessage = "IBAN is required.")]
    [StringLength(34, MinimumLength = 15, ErrorMessage = "IBAN must be between 15 and 34 characters.")]
    string Iban,

    [Range(0, double.MaxValue, ErrorMessage = "Initial balance cannot be negative.")]
    decimal InitialBalance,

    [Required]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Currency must be a 3-letter ISO code (e.g. EUR).")]
    string Currency
);
