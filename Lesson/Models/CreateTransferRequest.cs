using System.ComponentModel.DataAnnotations;

namespace Lesson.Models;

/// <summary>
/// Lesson 07-A — Data Annotations validation model.
///
/// Data Annotations are applied as attributes on model properties.
/// ASP.NET Core validates them automatically during model binding and
/// populates ModelState when rules are violated.
///
/// Java parallel: Bean Validation (JSR-380) — @NotNull, @Size, @Min, @Max on DTO fields.
/// Spring MVC calls the validator automatically when the controller parameter is @Valid.
/// </summary>
public class CreateTransferRequest
{
    [Required(ErrorMessage = "Source account number is required.")]
    [StringLength(20, MinimumLength = 5, ErrorMessage = "Account number must be 5-20 characters.")]
    public string FromAccount { get; set; } = string.Empty;

    [Required(ErrorMessage = "Destination account number is required.")]
    [StringLength(20, MinimumLength = 5, ErrorMessage = "Account number must be 5-20 characters.")]
    public string ToAccount { get; set; } = string.Empty;

    [Range(0.01, 1_000_000, ErrorMessage = "Amount must be between 0.01 and 1,000,000.")]
    public decimal Amount { get; set; }

    [MaxLength(200, ErrorMessage = "Description must not exceed 200 characters.")]
    public string? Description { get; set; }
}
