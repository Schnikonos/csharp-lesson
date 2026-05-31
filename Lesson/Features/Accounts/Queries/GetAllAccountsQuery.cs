using Lesson.Cqrs;
using Lesson.UnitOfWork;

namespace Lesson.Features.Accounts.Queries;

// ── Query ────────────────────────────────────────────────────────────────────
/// <summary>
/// Lesson 18-A — CQRS query: return all accounts as lightweight DTOs.
///
/// A query NEVER mutates state. The handler can read directly from the DB
/// without going through the full write-model aggregate — a read optimisation.
///
/// Java parallel:
///   Spring Data projection / @Query  →  query handler returning DTOs
///   Axon @QueryHandler               →  IRequestHandler&lt;IQuery&lt;T&gt;, T&gt;
/// </summary>
public record GetAllAccountsQuery : IQuery<IReadOnlyList<AccountSummaryDto>>;

public record AccountSummaryDto(
    int     Id,
    string  AccountNumber,
    string  OwnerName,
    decimal Balance);

// ── Handler ──────────────────────────────────────────────────────────────────
public class GetAllAccountsQueryHandler(IUnitOfWork uow)
    : MediatR.IRequestHandler<GetAllAccountsQuery, IReadOnlyList<AccountSummaryDto>>
{
    public async Task<IReadOnlyList<AccountSummaryDto>> Handle(
        GetAllAccountsQuery _,
        CancellationToken   ct)
    {
        var accounts = await uow.Accounts.GetAllAsync();
        return accounts.Select(a => new AccountSummaryDto(
            a.Id, a.AccountNumber, a.OwnerName, a.Balance)).ToList();
    }
}
