using FluentValidation;
using Lesson.Models;

namespace Lesson.Validators;

/// <summary>
/// Lesson 07-B — FluentValidation validator for CreateTransferRequest.
///
/// FluentValidation uses a fluent API to express rules in a dedicated class,
/// keeping validation logic out of the model and enabling complex, reusable rules.
///
/// Key differences from Data Annotations:
///   • Rules are defined in a separate class (Single Responsibility)
///   • Conditional rules: .When(), .Unless()
///   • Cross-property rules: RuleFor(x => x.To).NotEqual(x => x.From)
///   • Custom validators: Must(), MustAsync()
///
/// Java parallel: Hibernate Validator custom ConstraintValidator&lt;A, T&gt; — but
/// FluentValidation's API is significantly more expressive.
/// </summary>
public class CreateTransferRequestValidator : AbstractValidator<CreateTransferRequest>
{
    public CreateTransferRequestValidator()
    {
        RuleFor(x => x.FromAccount)
            .NotEmpty().WithMessage("Source account is required.")
            .Length(5, 20).WithMessage("Account number must be 5-20 characters.");

        RuleFor(x => x.ToAccount)
            .NotEmpty().WithMessage("Destination account is required.")
            .Length(5, 20).WithMessage("Account number must be 5-20 characters.")
            .NotEqual(x => x.FromAccount).WithMessage("Source and destination accounts must differ.");

        RuleFor(x => x.Amount)
            .InclusiveBetween(0.01m, 1_000_000m)
            .WithMessage("Amount must be between 0.01 and 1,000,000.");

        RuleFor(x => x.Description)
            .MaximumLength(200).WithMessage("Description must not exceed 200 characters.")
            .When(x => x.Description is not null);
    }
}
