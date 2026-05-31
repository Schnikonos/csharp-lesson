using FluentValidation;
using Lesson.Features.Accounts.Commands;

namespace Lesson.Validators;

/// <summary>
/// Lesson 18-B — FluentValidation validator for CreateAccountCommand.
/// Automatically picked up by ValidationBehavior via AddValidatorsFromAssembly.
/// </summary>
public class CreateAccountCommandValidator : AbstractValidator<CreateAccountCommand>
{
    public CreateAccountCommandValidator()
    {
        RuleFor(x => x.AccountNumber).NotEmpty().MaximumLength(50);
        RuleFor(x => x.OwnerName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AccountType).NotEmpty();
        RuleFor(x => x.InitialBalance).GreaterThanOrEqualTo(0);
    }
}
