using Lesson.Data;
using Lesson.Repositories;

namespace Lesson.UnitOfWork;

/// <summary>
/// Unit of Work implementation backed by BankingDbContext.
///
/// All repository operations within a single request share the same DbContext,
/// meaning they all participate in the same change-tracking session.
/// CommitAsync() calls SaveChangesAsync() once, flushing all pending changes
/// in a single SQL transaction.
///
/// Java parallel:
///   A Spring @Service with @Transactional creates an EntityManager (= one PU context)
///   for the duration of the method. CommitAsync() ≈ the implicit commit at method return.
///
/// Lifetime: Scoped — one instance per HTTP request, same as BankingDbContext.
/// </summary>
public class UnitOfWork(BankingDbContext db, IAccountRepository accounts) : IUnitOfWork
{
    public IAccountRepository Accounts { get; } = accounts;

    public Task<int> CommitAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);

    public void Dispose() => db.Dispose();
}
