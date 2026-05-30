# Lesson 04-C - Raw SQL, Stored Procedures, Compiled Queries, Split Queries

> **Branch:** `lesson/04-ef-advanced/c-advanced`
> **Prerequisites:** Lesson 04-B (Pagination, Projections, GroupBy, Any/All/Count)

---

## What you will learn

| Topic | C# / EF Core | Java / Spring Boot parallel |
|---|---|---|
| Raw SQL | `FromSqlRaw(sql, params)` | `@Query(nativeQuery = true)` |
| Stored procedure call | `FromSqlRaw("EXEC sp_… {0}", p)` | `@Procedure` / `EntityManager.createNativeQuery` |
| Compiled query | `EF.CompileAsyncQuery(…)` | `@NamedQuery` / `@NamedNativeQuery` |
| Split query | `.Include(…).AsSplitQuery()` | `@EntityGraph` with `SUBSELECT` fetch |
| Cartesian explosion | single JOIN ? N×M rows | N+1 / cartesian product in JPA `JOIN FETCH` |

---

## 1. FromSqlRaw — Hand-written Parameterised SQL

`FromSqlRaw` lets you write arbitrary SQL while still getting tracked entities back.
EF Core can compose additional LINQ operators (`Where`, `OrderBy`, `Include`, …) on top.

```csharp
var accounts = await db.BankAccounts
    .FromSqlRaw(
        "SELECT * FROM BankAccounts WHERE Balance > {0} AND IsDeleted = 0",
        minBalance)
    .OrderBy(a => a.AccountNumber)
    .ToListAsync();
```

> ?? **SQL injection:** always use `{0}` placeholders (or `SqlParameter` objects).
> Never interpolate user input directly into the string.
> `FromSqlInterpolated` is an alternative that accepts a C# interpolated string safely.

**Java parallel:** `@Query(value = "SELECT * FROM bank_accounts WHERE balance > :min", nativeQuery = true)`

---

## 2. Stored Procedure Calls

On **SQL Server**:

```csharp
var accounts = await db.BankAccounts
    .FromSqlRaw("EXEC sp_GetAccountByNumber {0}", accountNumber)
    .ToListAsync();
```

On **PostgreSQL**:

```csharp
var accounts = await db.BankAccounts
    .FromSqlRaw("SELECT * FROM sp_get_account_by_number({0})", accountNumber)
    .ToListAsync();
```

**SQLite** has no stored-procedure engine, so this lesson uses an equivalent parameterised
`SELECT` and documents the real SP syntax above.

**Java parallel:**
```java
@Procedure("sp_GetAccountByNumber")
BankAccount getByNumber(String accountNumber);
// or:
entityManager.createNativeQuery("CALL sp_GetAccountByNumber(?)", BankAccount.class)
             .setParameter(1, accountNumber)
             .getSingleResult();
```

---

## 3. Compiled Queries

Every time you call a LINQ query EF Core translates the expression tree to SQL.
`EF.CompileAsyncQuery` does that translation **once at startup** and caches the result,
eliminating the per-call overhead on hot paths (thousands of calls per second).

```csharp
// Declared as a static field — compiled once per AppDomain.
private static readonly Func<BankingDbContext, string, IAsyncEnumerable<BankAccount>>
    _getByNumber = EF.CompileAsyncQuery(
        (BankingDbContext ctx, string number) =>
            ctx.BankAccounts.Where(a => a.AccountNumber == number));

// Usage — no translation overhead on subsequent calls.
await foreach (var account in _getByNumber(db, accountNumber))
    return account;
```

**Java parallel:** Hibernate `@NamedQuery` / `@NamedNativeQuery` — compiled during
`SessionFactory` bootstrap and reused for every execution.

---

## 4. Split Queries — Preventing Cartesian Explosion

When you `Include` a collection navigation on multiple parent rows, EF Core's default
single-JOIN strategy produces a **Cartesian product**:

```
2 accounts × 5 transactions = 10 result rows transferred
(even though only 7 logical rows exist)
```

With large collections (100 parents × 1 000 children) this multiplies to **100 000 rows**
over the wire for what is logically 1 100 rows of data.

`AsSplitQuery()` fires two separate SELECTs and stitches the results in memory:

```csharp
var accounts = await db.BankAccounts
    .Include(a => a.Transactions)
    .AsSplitQuery()          // ? two queries instead of one JOIN
    .OrderBy(a => a.AccountNumber)
    .ToListAsync();
// SQL 1: SELECT * FROM BankAccounts
// SQL 2: SELECT * FROM Transactions WHERE BankAccountId IN (1, 2, …)
```

**Trade-off:** two round-trips instead of one; results may be slightly inconsistent if
another transaction modifies data between the two SELECTs.  Choose split queries when
collection sizes make the Cartesian product impractical.

**Java parallel:** `@EntityGraph` with `@EntityGraph.EntityGraphType.FETCH` and
`fetchType = SUBSELECT` on the collection mapping.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/accounts/raw?minBalance=0` | `FromSqlRaw` parameterised query |
| `GET` | `/accounts/by-number-sp/{number}` | Stored-procedure simulation |
| `GET` | `/accounts/by-number-compiled/{number}` | Compiled query lookup |
| `GET` | `/accounts/with-transactions` | `AsSplitQuery` — accounts + transactions |

---

## Project Structure (new / changed files)

```
Lesson/
  Repositories/
    IAccountRepository.cs     + GetByRawSqlAsync, GetByNumberStoredProcAsync,
                                GetByNumberCompiledAsync, GetWithTransactionsSplitAsync
    AccountRepository.cs      implements the above; compiled query as static field
  Controllers/
    AccountDtos.cs            + TransactionSummaryDto
    AccountsController.cs     + /raw, /by-number-sp, /by-number-compiled,
                                /with-transactions endpoints
Lesson.Tests/
  AccountsControllerRawSqlTests.cs  NEW  10 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~AccountsControllerRawSqlTests"
# 10 tests — all pass
```

| Test | What it verifies |
|---|---|
| `GetByRawSql_BelowAllBalances_ReturnsAllAccounts` | `FromSqlRaw` with threshold 0 returns all seeded accounts |
| `GetByRawSql_AboveAllBalances_ReturnsEmpty` | impossibly high threshold ? empty list |
| `GetByRawSql_OnlyReturnsAccountsAboveThreshold` | all returned accounts exceed the threshold |
| `GetByNumberStoredProc_ExistingAccount_ReturnsAccount` | SP simulation returns the right account |
| `GetByNumberStoredProc_UnknownAccount_ReturnsNotFound` | 404 for unknown account number |
| `GetByNumberCompiled_ExistingAccount_ReturnsAccount` | compiled query finds seeded account |
| `GetByNumberCompiled_UnknownAccount_ReturnsNotFound` | 404 for unknown account number |
| `GetByNumberCompiled_CalledTwice_BothReturnSameResult` | compiled query is idempotent |
| `GetWithTransactions_ReturnsAccountsWithTransactions` | split query returns accounts |
| `GetWithTransactions_SeededTransactions_ArePresent` | transactions are present in the split-query result |

---

## Exercises

1. Replace `FromSqlRaw` in `GetByRawSqlAsync` with `FromSqlInterpolated` and observe how EF Core automatically prevents SQL injection.
2. Add a `ExecuteSqlRaw` call to bulk-deactivate all accounts of a given type:
   `UPDATE BankAccounts SET IsActive = 0 WHERE AccountType = {0}`.
3. Benchmark (using BenchmarkDotNet) the compiled query vs. a non-compiled equivalent to
   measure the translation overhead on a tight loop.
4. Enable `QuerySplittingBehavior.SplitQuery` globally in `DbContextOptions` and verify that all `Include` calls now use split queries by default, then override one back to `AsSingleQuery()`.


> **Branch:** `lesson/04-ef-advanced/b-intermediate`
> **Prerequisites:** Lesson 04-A (Navigation properties, Include / ThenInclude)

---

## What you will learn

| Topic | C# / EF Core | Java / Spring Boot parallel |
|---|---|---|
| Pagination | `Skip` / `Take` | `PageRequest.of(page, size)` ? `Page<T>` |
| Projection | `.Select(a => new Dto(...))` | DTO projection with `@Query` + constructor expression |
| GroupBy aggregate | `.GroupBy(...).Select(g => ...)` | `@Query("SELECT ... GROUP BY ...")` |
| Existential check | `AnyAsync(predicate)` | `repository.existsBy...()` |
| Universal check | `AllAsync(predicate)` | custom `@Query` with NOT EXISTS |
| Scalar count | `CountAsync(predicate)` | `repository.countBy...()` |
| IQueryable vs IEnumerable | query tree vs in-memory iteration | `JpaSpecificationExecutor` vs stream |

---

## 1. IQueryable vs IEnumerable

**Key concept:** `IQueryable<T>` is an unevaluated expression tree — EF Core composes SQL
from it and executes when you call a terminal operator (`ToListAsync`, `FirstOrDefaultAsync`, …).
`IEnumerable<T>` is in-memory — all rows are loaded before filtering/projecting.

```csharp
// IQueryable — SQL WHERE is added before the query is sent
IQueryable<BankAccount> query = db.BankAccounts;
query = query.Where(a => a.AccountType == "Savings"); // no DB round-trip yet
var list = await query.ToListAsync();                  // ONE SQL query with WHERE

// IEnumerable — loads ALL rows, then filters in C#
IEnumerable<BankAccount> all = await db.BankAccounts.ToListAsync();
var savings = all.Where(a => a.AccountType == "Savings"); // in-memory!
```

**Rule:** keep your queries as `IQueryable` until you need the data.

**Java parallel:** `JpaSpecificationExecutor<T>` builds a query spec lazily; calling
`findAll(spec)` executes it. Collecting to a `List<>` and then streaming is the in-memory equivalent.

---

## 2. Pagination with Skip / Take

```csharp
var items = await db.BankAccounts
    .OrderBy(a => a.AccountNumber)      // ORDER BY is required before OFFSET
    .Skip((page - 1) * pageSize)        // SQL: OFFSET
    .Take(pageSize)                     // SQL: LIMIT / FETCH NEXT
    .ToListAsync();
```

Combined with a total count this gives a `PagedResult<T>`:

```csharp
int total = await baseQuery.CountAsync();
// ... Skip / Take ...
return new PagedResult<T>(items, total, page, pageSize);
```

**Java parallel:** `repository.findAll(PageRequest.of(page - 1, size))` returns a `Page<T>`
with `.getContent()`, `.getTotalElements()`, and `.getTotalPages()`.

---

## 3. Projection with Select

Only fetch the columns you actually need:

```csharp
var dtos = await db.BankAccounts
    .Select(a => new AccountSummaryDto(
        a.Id, a.AccountNumber, a.OwnerName, a.AccountType, a.Balance, a.IsActive))
    .ToListAsync();
// SQL: SELECT Id, AccountNumber, OwnerName, AccountType, Balance, IsActive FROM BankAccounts
```

Audit / concurrency columns (`RowVersion`, `CreatedAt`, `UpdatedAt`) are never touched.

**Java parallel:** constructor expression in JPQL:
`SELECT new com.example.dto.AccountSummaryDto(a.id, a.accountNumber, ...) FROM BankAccount a`

---

## 4. GroupBy Aggregate

```csharp
// Works fully in SQL on SQL Server / PostgreSQL
var stats = await db.BankAccounts
    .GroupBy(a => a.AccountType)
    .Select(g => new AccountTypeStatDto(
        g.Key, g.Count(), g.Sum(a => a.Balance), g.Average(a => (double)a.Balance)))
    .ToListAsync();
```

> **SQLite caveat:** SQLite's `decimal` support for aggregates is limited.
> The implementation fetches `(AccountType, Balance)` columns via `IQueryable` projection,
> then completes the grouping in C# memory — illustrating the intentional IQueryable ? IEnumerable handoff:
>
> ```csharp
> var rows = await db.BankAccounts
>     .Select(a => new { a.AccountType, a.Balance })
>     .ToListAsync();                                   // IQueryable terminates here
>
> return rows
>     .GroupBy(a => a.AccountType)                     // IEnumerable GroupBy in C#
>     .Select(g => new AccountTypeStatDto(...))
>     .ToList();
> ```

**Java parallel:** `@Query("SELECT a.accountType, COUNT(a), SUM(a.balance), AVG(a.balance) FROM BankAccount a GROUP BY a.accountType")`

---

## 5. Any / All / Count

```csharp
// Any — SQL: SELECT CASE WHEN EXISTS (...) THEN 1 ELSE 0 END
bool hasHighBalance = await db.BankAccounts.AnyAsync(a => a.Balance > threshold);

// All — SQL: SELECT CASE WHEN NOT EXISTS (... WHERE NOT condition) THEN 1 ELSE 0 END
bool allPositive = await db.BankAccounts.AllAsync(a => a.Balance > 0);

// Count — SQL: SELECT COUNT(*) FROM BankAccounts WHERE IsActive = 1
int active = await db.BankAccounts.CountAsync(a => a.IsActive);
```

None of these load entity rows — they return a single scalar from the database.

**Java parallel:**
- `AnyAsync` ? `repository.existsByBalanceGreaterThan(threshold)`
- `AllAsync` ? custom `@Query` with `NOT EXISTS`
- `CountAsync` ? `repository.countByIsActive(true)`

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/accounts/summary?page=1&pageSize=10` | Paginated + projected DTO list |
| `GET` | `/accounts/stats` | GroupBy AccountType with COUNT / SUM / AVG |
| `GET` | `/accounts/any-high-balance?threshold=10000` | AnyAsync — returns `true`/`false` |
| `GET` | `/accounts/all-positive` | AllAsync — returns `true`/`false` |
| `GET` | `/accounts/count?type=Savings` | CountAsync with optional type filter |

---

## Project Structure (new / changed files)

```
Lesson/
  Entities/
    Transaction.cs               NEW  linked to BankAccount (one-to-many)
    BankAccount.cs                    + Transactions navigation collection
  Repositories/
    IAccountRepository.cs             + GetPagedSummariesAsync, GetStatsByTypeAsync,
                                        AnyWithBalanceAboveAsync, AllPositiveBalanceAsync,
                                        CountActiveAsync
    AccountRepository.cs              implements the above; shows IQueryable/IEnumerable split
  Controllers/
    AccountDtos.cs                    + AccountSummaryDto, AccountTypeStatDto, PagedResult<T>
    AccountsController.cs             + /summary, /stats, /any-high-balance,
                                        /all-positive, /count endpoints
  Data/
    BankingDbContext.cs               + DbSet<Transaction>, HasMany/WithOne config,
                                        seed transactions, query filter on Transaction
    Migrations/
      AddTransactions            NEW  Transactions table + seed rows
Lesson.Tests/
  AccountsControllerAdvancedTests.cs  NEW  11 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~AccountsControllerAdvancedTests"
# 11 tests — all pass
```

| Test | What it verifies |
|---|---|
| `GetSummary_Page1_ReturnsPagedResult` | Pagination works; page/count fields populated |
| `GetSummary_PageSize1_ReturnsSingleItem` | `Take(1)` returns exactly one item |
| `GetSummary_ProjectionDto_DoesNotExposeAuditFields` | Projection omits audit columns |
| `GetSummary_InvalidPage_ReturnsBadRequest` | `page=0` ? 400 |
| `GetStats_ReturnsGroupedByAccountType` | GroupBy returns non-empty stat rows |
| `GetStats_SeededData_ContainsCheckingAndSavings` | Both seeded types appear |
| `AnyHighBalance_BelowSeedValues_ReturnsTrue` | AnyAsync ? true when threshold is below seed balances |
| `AnyHighBalance_AboveSeedValues_ReturnsFalse` | AnyAsync ? false for impossibly high threshold |
| `AllPositive_SeededAccounts_ReturnsTrue` | AllAsync ? true; all seeded accounts have positive balance |
| `CountActive_NoFilter_ReturnsAllActiveAccounts` | CountAsync ? > 0 |
| `CountActive_FilteredByType_SumsToTotal` | Savings + Checking counts equal total |

---

## Exercises

1. Add a `GET /accounts/summary` sort parameter (`sortBy=balance&desc=true`) using conditional `OrderBy` on `IQueryable`.
2. Add `GET /accounts/stats/transactions` — GroupBy `AccountType` with `SUM` of transaction amounts (requires a join to `Transactions`).
3. Implement server-side GroupBy for a provider that supports it (e.g., SQL Server) and compare the generated SQL to the SQLite fallback.
4. Add a `MinBalance` / `MaxBalance` filter to `/accounts/summary` and observe how adding `.Where()` clauses to an `IQueryable` before `Skip`/`Take` pushes the filter into the SQL `WHERE` clause.

| Eager loading | `.Include(c => c.Accounts)` | `@EntityGraph` / `JOIN FETCH` |
| Chained loading | `.ThenInclude(a => a.Address)` | nested `JOIN FETCH` in JPQL |
| Filtered Include | `.Include(c => c.Accounts.Where(a => a.IsActive))` | `@Query` with WHERE on the join |
| FK assignment | set `account.CustomerId` — EF updates the row | set the `@ManyToOne` field + persist |
| Lazy loading | disabled by default — must call `Include` explicitly | `FetchType.LAZY` (default in JPA) |

---

## 1. Navigation Properties

A navigation property is a C# reference or collection on an entity class that EF Core uses
to represent a relationship in the object model.

EF Core maps this to a foreign key column (`CustomerId`) in `BankAccounts`.

```csharp
// Customer -- the "one" side
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public ICollection<BankAccount> Accounts { get; set; } = [];
}

// BankAccount -- the "many" side
public class BankAccount
{
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
}
```

Configure in `OnModelCreating`:

```csharp
modelBuilder.Entity<Customer>()
    .HasMany(c => c.Accounts)
    .WithOne(a => a.Customer)
    .HasForeignKey(a => a.CustomerId)
    .IsRequired(false)
    .OnDelete(DeleteBehavior.SetNull);
```

**Java parallel:**
```java
@OneToMany(mappedBy = "customer", cascade = CascadeType.ALL)
private List<BankAccount> accounts = new ArrayList<>();

@ManyToOne @JoinColumn(name = "customer_id")
private Customer customer;
```

---

## 2. Eager Loading with Include

By default, EF Core does **not** lazy-load navigation properties. You must call `.Include()` explicitly.

```csharp
var customer = await db.Customers
    .Include(c => c.Accounts)
    .FirstOrDefaultAsync(c => c.Id == id);
```

**Java parallel:** `@EntityGraph(attributePaths = "accounts")` or `JOIN FETCH` in JPQL.

---

## 3. ThenInclude — Loading Nested Navigation

```csharp
var customer = await db.Customers
    .Include(c => c.Accounts)
        .ThenInclude(a => a.Address)
    .FirstOrDefaultAsync(c => c.Id == id);
```

**Java parallel:** nested `JOIN FETCH` in JPQL.

---

## 4. Filtered Include

```csharp
var customer = await db.Customers
    .Include(c => c.Accounts.Where(a => a.IsActive))
    .FirstOrDefaultAsync(c => c.Id == id);
```

The WHERE predicate is pushed into the SQL JOIN. The global `IsDeleted` query filter is also
applied automatically to included collections.

---

## 5. FK Assignment (linking records)

```csharp
account.CustomerId = customerId;
await uow.CommitAsync();
// SQL: UPDATE BankAccounts SET CustomerId = @id WHERE Id = @accountId
```

Setting the FK scalar property is enough — EF Core keeps navigation in sync within the same DbContext scope.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/customers` | All customers (no accounts loaded) |
| `GET` | `/customers/{id}/accounts` | Customer + all accounts (Include + ThenInclude) |
| `GET` | `/customers/{id}/accounts/active` | Customer + active accounts (filtered Include) |
| `POST` | `/customers` | Create customer; 201 / 409 on duplicate email |
| `POST` | `/customers/{cid}/accounts/{aid}` | Assign existing account to customer (FK update) |

---

## Project Structure (new / changed files)

```
Lesson/
  Entities/
    Customer.cs                  NEW  "one" side of Customer -> BankAccounts
    BankAccount.cs                    + CustomerId (FK) + Customer? (navigation)
  Repositories/
    ICustomerRepository.cs       NEW  interface with Include-based query methods
    CustomerRepository.cs        NEW  demonstrates Include, ThenInclude, filtered Include
  Controllers/
    CustomerDtos.cs              NEW  CreateCustomerRequest, CustomerResponse
    CustomersController.cs       NEW  CRUD + Include/filtered-Include endpoints
  Data/
    BankingDbContext.cs               + DbSet<Customer>, HasMany/WithOne config, updated seed
    Migrations/
      AddCustomerNavigation      NEW  adds Customers table + CustomerId FK column
Lesson.Tests/
  CustomersControllerTests.cs   NEW  7 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~CustomersControllerTests"
# 7 tests — all pass
```

| Test | What it verifies |
|---|---|
| `GetAll_ReturnsSeededCustomers` | Seeded customers are returned |
| `Create_ValidRequest_ReturnsCreated` | 201 + id populated |
| `Create_DuplicateEmail_ReturnsConflict` | 409 on duplicate email |
| `GetWithAccounts_SeededCustomer_ReturnsAccounts` | Include fires — Accounts is not empty |
| `GetWithAccounts_MissingCustomer_ReturnsNotFound` | 404 for unknown customer |
| `GetWithActiveAccounts_ReturnsOnlyActiveAccounts` | Filtered Include — inactive account excluded |
| `AssignAccount_LinksAccountToCustomer` | FK assignment — account appears in customer list |

---

## Exercises

1. Add `GET /customers/{id}/accounts/savings` using a filtered include for `AccountType == "Savings"`.
2. Add `GET /customers?includeAccounts=true` — conditionally apply Include only when requested.
3. Explore `AsSplitQuery()`: replace the default single-JOIN strategy with two separate queries and compare SQL logs.
4. Add a `Transaction` entity linked to `BankAccount` and practice `ThenInclude` two levels deep:
   `Customer -> Accounts -> Transactions`.
