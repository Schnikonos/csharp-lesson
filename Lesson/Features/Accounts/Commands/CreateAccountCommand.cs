using Lesson.Cqrs;
using Lesson.Entities;
using Lesson.UnitOfWork;

namespace Lesson.Features.Accounts.Commands;

// ── Command ─────────────────────────────────────────────────────────────────
/// <summary>
/// Lesson 18-A — CQRS command: create a new bank account.
///
/// Key concepts:
///   ICommand&lt;TResult&gt;  — marker interface; signals "this changes state".
///   ICommandHandler     — processes the command inside a Use-Case boundary.
///   The controller only knows about the command record; business logic lives here.
///
/// Java parallel:
///   Axon @CommandHandler  →  IRequestHandler&lt;TCommand, TResult&gt;
///   Spring @Transactional service method  →  handler + SaveChangesAsync
/// </summary>
public record CreateAccountCommand(
    string  AccountNumber,
    string  OwnerName,
    string  AccountType,
    decimal InitialBalance) : ICommand<int>;   // returns the new account id

// ── Handler ──────────────────────────────────────────────────────────────────
public class CreateAccountCommandHandler(IUnitOfWork uow)
    : MediatR.IRequestHandler<CreateAccountCommand, int>
{
    public async Task<int> Handle(CreateAccountCommand cmd, CancellationToken ct)
    {
        var account = new BankAccount
        {
            AccountNumber = cmd.AccountNumber,
            OwnerName     = cmd.OwnerName,
            AccountType   = cmd.AccountType,
            Balance       = cmd.InitialBalance,
        };

        await uow.Accounts.AddAsync(account);
        await uow.CommitAsync(ct);
        return account.Id;
    }
}
