using FluentValidation;
using Lesson.Exceptions;
using MediatR;

namespace Lesson.Commands;

// ── Command (IRequest<T>) ──────────────────────────────────────────────────────

/// <summary>
/// Lesson 07-C — MediatR command for creating a payment.
///
/// IRequest&lt;T&gt; is MediatR's "command" or "query" contract.
/// Java parallel: a Spring @Service method parameter — the command is the DTO
/// you pass to the service; MediatR decouples caller from handler.
/// </summary>
public record CreatePaymentCommand(
    string FromAccount,
    string ToAccount,
    decimal Amount) : IRequest<PaymentResult>;

public record PaymentResult(Guid Id, string FromAccount, string ToAccount, decimal Amount);

// ── FluentValidation for the command ──────────────────────────────────────────

public class CreatePaymentCommandValidator : AbstractValidator<CreatePaymentCommand>
{
    public CreatePaymentCommandValidator()
    {
        RuleFor(x => x.FromAccount).NotEmpty().Length(5, 20);
        RuleFor(x => x.ToAccount).NotEmpty().Length(5, 20)
            .NotEqual(x => x.FromAccount).WithMessage("Accounts must differ.");
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(1_000_000);
    }
}

// ── Handler (IRequestHandler<TRequest, TResponse>) ────────────────────────────

/// <summary>
/// The command handler contains the business logic.
/// Domain exceptions thrown here are caught by DomainExceptionHandler globally.
/// </summary>
public class CreatePaymentHandler : IRequestHandler<CreatePaymentCommand, PaymentResult>
{
    private static readonly HashSet<string> _blockedAccounts = ["BLOCKED01"];

    public Task<PaymentResult> Handle(
        CreatePaymentCommand request,
        CancellationToken cancellationToken)
    {
        if (_blockedAccounts.Contains(request.FromAccount))
            throw new BusinessRuleException($"Account {request.FromAccount} is blocked.");

        var result = new PaymentResult(
            Id: Guid.NewGuid(),
            FromAccount: request.FromAccount,
            ToAccount: request.ToAccount,
            Amount: request.Amount);

        return Task.FromResult(result);
    }
}
