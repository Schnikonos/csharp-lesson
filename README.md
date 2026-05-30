# Lesson 03-C — Unit of Work, Optimistic Concurrency, Soft Delete & Audit Fields

> **Branch:** `lesson/03-ef-crud/c-advanced`
> **Prerequisites:** Lesson 03-B (Repository pattern, IQueryable, owned entities)

---

## What you will learn

| Topic | C# / EF Core | Java / Spring Boot parallel |
|---|---|---|
| Unit of Work | `IUnitOfWork` + single `CommitAsync()` | `@Transactional` on a service method |
| Optimistic concurrency | `[Timestamp]` / `RowVersion` + `DbUpdateConcurrencyException` | `@Version` + `ObjectOptimisticLockingFailureException` |
| Soft delete | `IsDeleted` flag + `HasQueryFilter` | `@SQLDelete` + `@Where` |
| Global query filter | `modelBuilder.Entity<T>().HasQueryFilter(...)` | Hibernate `@Filter` / `@Where` |
| Audit fields | `SaveChangesAsync` override stamps `CreatedAt`, `UpdatedAt`, `UpdatedBy` | `@PrePersist` / `@LastModifiedDate` / `@LastModifiedBy` |

---

## 1. Unit of Work

`DbContext` itself is an implementation of the Unit of Work pattern, but it is useful to wrap
it in an explicit `IUnitOfWork` so that:

- The controller calls `CommitAsync()` once at the end — one SQL transaction boundary.
- Multiple repositories can participate in the same transaction without each calling `SaveChangesAsync`.
- The seam between controller and persistence is explicit and easily mockable in unit tests.

```csharp
// IUnitOfWork.cs
public interface IUnitOfWork : IDisposable
{
    IAccountRepository Accounts { get; }
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}

// Registration in Program.cs (Scoped — same lifetime as DbContext)
builder.Services.AddScoped<IAccountRepository, AccountRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Controller injects IUnitOfWork instead of the repository directly
public class AccountsController(IUnitOfWork uow) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateAccountRequest req)
    {
        await uow.Accounts.AddAsync(new BankAccount { ... });
        await uow.CommitAsync(); // single SaveChangesAsync call
        return Created(...);
    }
}
```

**Java parallel:** a `@Service` method annotated `@Transactional` creates a single
`EntityManager` / persistence context; all repository calls inside it share that context
and are committed when the method returns.

---

## 2. Optimistic Concurrency with RowVersion

Optimistic concurrency assumes conflicts are rare. Instead of locking the row, EF Core reads a
version token and includes it in every `UPDATE` / `DELETE` WHERE clause. If another transaction
has already changed the row, the WHERE finds no row, EF Core throws `DbUpdateConcurrencyException`,
and the application returns HTTP 409 Conflict.

```csharp
// BankAccount.cs
[Timestamp] // EF Core maps this as a row-version concurrency token
public byte[] RowVersion { get; set; } = [];

// AccountsController.cs — catch the exception on PUT
try
{
    await uow.CommitAsync();
}
catch (DbUpdateConcurrencyException)
{
    return Conflict(new { Error = "The account was modified by another request. Please reload and retry." });
}
```

**Java parallel:**
```java
@Version
private byte[] rowVersion;
// Spring Data throws ObjectOptimisticLockingFailureException on conflict
```

---

## 3. Soft Delete + Global Query Filter

Instead of issuing a SQL `DELETE`, a soft delete sets `IsDeleted = true`. A **global query filter**
then automatically appends `WHERE IsDeleted = 0` to every LINQ query on that entity type.

```csharp
// OnModelCreating — one line activates the filter everywhere
modelBuilder.Entity<BankAccount>()
    .HasQueryFilter(a => !a.IsDeleted);

// Repository — regular queries are filtered automatically
public async Task<IReadOnlyList<BankAccount>> GetAllAsync(...)
    => await db.BankAccounts           // global filter applied
               .Where(...)
               .ToListAsync();

// Repository — bypass the filter to see deleted rows
public async Task<IReadOnlyList<BankAccount>> GetDeletedAsync()
    => await db.BankAccounts
               .IgnoreQueryFilters()   // bypass IsDeleted filter
               .Where(a => a.IsDeleted)
               .ToListAsync();

// Soft delete — just set the flag; CommitAsync() persists it
public Task SoftDeleteAsync(BankAccount a) { a.IsDeleted = true; return Task.CompletedTask; }
```

**Java parallel:** `@SQLDelete(sql = "UPDATE bank_accounts SET is_deleted=true WHERE id=?")` +
`@Where(clause = "is_deleted=false")` on the entity class.

---

## 4. Audit Fields via SaveChangesAsync Override

`BankingDbContext` overrides `SaveChangesAsync` to inspect the EF Core change tracker and stamp
`CreatedAt` / `UpdatedAt` / `UpdatedBy` automatically on every save — no controller code needed.

```csharp
public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
{
    var now = DateTime.UtcNow;

    foreach (var entry in ChangeTracker.Entries<BankAccount>())
    {
        if (entry.State == EntityState.Added)
            entry.Entity.CreatedAt = now;

        if (entry.State is EntityState.Added or EntityState.Modified)
        {
            entry.Entity.UpdatedAt  = now;
            entry.Entity.UpdatedBy ??= "system"; // replace with current user in real apps
        }
    }

    return base.SaveChangesAsync(cancellationToken);
}
```

**Java parallel:** `@PrePersist` / `@PreUpdate` lifecycle callbacks on the entity, or Spring Data's
`@CreatedDate` / `@LastModifiedDate` / `@LastModifiedBy` with an `AuditorAware<String>` bean.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/accounts` | Non-deleted accounts (global filter active) |
| `GET` | `/accounts?type=Savings` | IQueryable filter + soft-delete filter |
| `GET` | `/accounts/deleted` | Soft-deleted rows (`IgnoreQueryFilters`) |
| `GET` | `/accounts/{id}` | 200 / 404 |
| `POST` | `/accounts` | Creates; audit fields stamped automatically |
| `PUT` | `/accounts/{id}` | Updates; 409 on concurrency conflict |
| `DELETE` | `/accounts/{id}` | **Soft** delete (sets `IsDeleted = true`) |
| `POST` | `/accounts/{id}/restore` | Restores a soft-deleted account |

---

## Project Structure (new / changed files)

```
Lesson/
  Entities/
    BankAccount.cs          + RowVersion, IsDeleted, UpdatedAt, UpdatedBy
  UnitOfWork/
    IUnitOfWork.cs          NEW  Unit of Work interface
    UnitOfWork.cs           NEW  implementation backed by BankingDbContext
  Repositories/
    IAccountRepository.cs   + GetDeletedAsync, SoftDeleteAsync, RestoreAsync
    AccountRepository.cs    + IgnoreQueryFilters, soft-delete helpers; SaveChanges removed
  Data/
    BankingDbContext.cs      + HasQueryFilter + SaveChangesAsync audit override
    Migrations/
      AddAdvancedFields      NEW  adds RowVersion, IsDeleted, UpdatedAt, UpdatedBy columns
  Controllers/
    AccountDtos.cs           + UpdatedAt, UpdatedBy fields on AccountResponse
    AccountsController.cs    injects IUnitOfWork; soft-delete / restore / concurrency handling
Lesson.Tests/
  AccountsControllerTests.cs  + 3 new 03-C tests (15 total)
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~AccountsControllerTests"
# 15 tests — all pass
```

| Test | What it verifies |
|---|---|
| `GetAll_ReturnsSeededAccounts` | List returns seeded rows |
| `GetById_ExistingId_ReturnsAccount` | Correct entity for seed id=1 |
| `GetById_MissingId_ReturnsNotFound` | 404 for unknown id |
| `Create_ValidRequest_ReturnsCreated` | 201 + Location header + populated id |
| `Create_DuplicateAccountNumber_ReturnsConflict` | 409 on duplicate account number |
| `Update_ExistingAccount_ReturnsUpdatedData` | 200 + updated fields |
| `Delete_ExistingAccount_ReturnsNoContent` | 204 + subsequent GET = 404 |
| `GetAll_FilterByType_ReturnsOnlyMatchingAccounts` | `?type=Savings` SQL filter |
| `GetAll_FilterByType_UnknownType_ReturnsEmptyList` | Unknown type returns `[]` |
| `Create_WithAddress_AddressRoundTrips` | Owned Address persisted & returned |
| `Update_WithAddress_AddressIsPersisted` | Address can be added via PUT |
| `Create_WithoutAddress_AddressIsNull` | `address` is null when not provided |
| `Create_SetsAuditFields` ⭐ | `UpdatedAt` and `UpdatedBy` stamped on insert |
| `Delete_SoftDeletes_HiddenFromGetAll` ⭐ | Soft-deleted row hidden; visible via `/deleted` |
| `Restore_MakesAccountVisibleAgain` ⭐ | Restored account is visible in `GET /accounts/{id}` |

---

## Exercises

1. Pass the current user name from `IHttpContextAccessor` to `UpdatedBy` in the `SaveChangesAsync` override.
2. Add `DELETE /accounts/{id}/hard` that performs a real SQL `DELETE` — compare with soft delete.
3. Simulate a concurrency conflict in a test: load the same entity twice, update both, commit the second one, and assert HTTP 409.
4. Add a `CreatedBy` audit field that is set only on insert (never overwritten).
5. Extend the global query filter to also exclude inactive accounts (`IsActive = false`) and verify with a test.

