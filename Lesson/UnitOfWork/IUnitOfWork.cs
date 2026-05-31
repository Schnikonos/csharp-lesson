using Lesson.Repositories;

namespace Lesson.UnitOfWork;

/// <summary>
/// Unit of Work pattern — groups one or more repository operations into a single
/// database transaction that is committed or rolled back together.
///
/// Java parallel:
///   @Transactional on a Spring service method implicitly wraps all repository
///   calls in one transaction.  Here we make that boundary explicit.
///
/// Use it when a single HTTP request must modify several aggregates atomically:
///   await _uow.Accounts.AddAsync(newAccount);
///   await _uow.CommitAsync();     // one SQL transaction, one call to SaveChangesAsync
/// </summary>
public interface IUnitOfWork : IDisposable
{
    IAccountRepository Accounts { get; }

    /// <summary>
    /// Persists all pending changes tracked by the DbContext.
    /// Equivalent to EntityManager.flush() + transaction.commit() in JPA.
    /// </summary>
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
