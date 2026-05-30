# Lesson 03-B — Repository Pattern, IQueryable vs IEnumerable, Owned Entities

> **Branch:** `lesson/03-ef-crud/b-intermediate`
> **Prerequisites:** Lesson 03-A (DbContext, basic CRUD, migrations)

---

## What you will learn

| Topic | C# / ASP.NET Core | Java / Spring Boot parallel |
|---|---|---|
| Repository pattern | `IAccountRepository` + `AccountRepository` | `@Repository` interface + `JpaRepository` |
| IQueryable composition | `.Where()` before `.ToListAsync()` = SQL WHERE | Spring Data `Specification<T>` |
| IEnumerable pitfall | `.ToList()` early = all rows loaded, then filtered in C# | `findAll()` + stream filter |
| Owned entities | `OwnsOne<Address>()` — columns in the same table | `@Embeddable` / `@Embedded` in JPA |
| Value objects | `Address` with no identity of its own | JPA `@Embeddable` class |

---

## 1. Repository Pattern

The repository pattern inserts a named interface between the controller and the database.
The controller declares *what* it needs; the concrete class decides *how* EF Core satisfies it.

```
Controller  -->  IAccountRepository  <--  AccountRepository (wraps BankingDbContext)
```

**Why bother?**
- The controller can be unit-tested with a fake repository — no database required.
- All EF Core details (`DbSet`, `SaveChangesAsync`) are encapsulated in one class.
- Switching the storage backend (e.g. SQLite to PostgreSQL) needs no controller changes.

**Java parallel:** a `@Repository` interface extending `JpaRepository<BankAccount, Integer>`.

```csharp
// Interface — what the controller sees
public interface IAccountRepository
{
    Task<IReadOnlyList<BankAccount>> GetAllAsync(string? accountType = null);
    Task<BankAccount?> GetByIdAsync(int id);
    Task<bool> ExistsAsync(string accountNumber);
    Task<BankAccount> AddAsync(BankAccount account);
    Task UpdateAsync(BankAccount account);
    Task DeleteAsync(BankAccount account);
}

// Registration in Program.cs — same Scoped lifetime as DbContext
builder.Services.AddScoped<IAccountRepository, AccountRepository>();

// Constructor injection in the controller
public class AccountsController(IAccountRepository repo) : ControllerBase { }
```

---

## 2. IQueryable vs IEnumerable

This is one of the most important EF Core distinctions for a Java developer to internalise.

### IQueryable — the query is SQL

`IQueryable<T>` holds an *expression tree*. EF Core translates every `.Where()`, `.OrderBy()`,
and `.Select()` you chain onto it into SQL clauses. The database executes the query;
only matching rows travel over the wire.

```csharp
// SQL produced: SELECT * FROM BankAccounts WHERE AccountType = 'Savings' ORDER BY AccountNumber
IQueryable<BankAccount> query = db.BankAccounts;

if (!string.IsNullOrWhiteSpace(accountType))
    query = query.Where(a => a.AccountType == accountType); // adds WHERE — no DB call yet

return await query.OrderBy(a => a.AccountNumber).ToListAsync(); // ONE round-trip to DB
```

### IEnumerable — data is already in C# memory

Calling `.ToListAsync()` (or `.AsEnumerable()`) materialises all rows immediately.
Any further LINQ runs as a C# loop over the loaded objects — not as SQL.

```csharp
// LOADS ALL ROWS, then filters in C# — avoid this pattern
IEnumerable<BankAccount> all = await db.BankAccounts.ToListAsync();
var filtered = all.Where(a => a.AccountType == accountType); // C# in-memory, no SQL filter
```

**Rule:** keep the query as `IQueryable<T>` and only materialise it (via `ToListAsync`,
`FirstOrDefaultAsync`, etc.) at the last possible moment.

**Java parallel:**
- `IQueryable` composition ≈ Spring Data `Specification<T>` or a derived query method building.
- Early `.AsEnumerable()` ≈ calling `findAll()` and then `.stream().filter(...)` in Java.

---

## 3. Owned Entities (Value Objects)

An owned entity has no independent identity. EF Core stores it in the **same table**
as its owner — no join, no FK to a separate table, no `Id` column of its own.

```csharp
// Value object — notice: no Id
public class Address
{
    public string Street     { get; set; } = string.Empty;
    public string City       { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country    { get; set; } = string.Empty;
}

// Owner
public class BankAccount
{
    // ...
    public Address? Address { get; set; } // null = no address on file
}
```

Configure with the Fluent API in `OnModelCreating`:

```csharp
// Columns Address_Street, Address_City, Address_PostalCode, Address_Country
// are added directly to the BankAccounts table.
modelBuilder.Entity<BankAccount>().OwnsOne(a => a.Address);
```

A new `AddOwnedAddress` migration adds the four columns — no `Addresses` table is created.

**Java parallel:** `@Embeddable` class + `@Embedded` field on the owning `@Entity`.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/accounts` | All accounts, ordered by `AccountNumber` |
| `GET` | `/accounts?type=Savings` | IQueryable filter — one SQL WHERE clause |
| `GET` | `/accounts/{id}` | 200 / 404 |
| `POST` | `/accounts` | Optional `address` in body; 201 / 409 |
| `PUT` | `/accounts/{id}` | Optional `address` in body; 200 / 404 |
| `DELETE` | `/accounts/{id}` | 204 / 404 |

---

## Project Structure (new / changed files)

```
Lesson/
  Entities/
    Address.cs                    NEW  owned value object
    BankAccount.cs                     + Address? property
  Repositories/
    IAccountRepository.cs         NEW  interface
    AccountRepository.cs          NEW  EF Core implementation (IQueryable demo inside)
  Data/
    BankingDbContext.cs                 + OwnsOne<Address> in OnModelCreating
    Migrations/
      AddOwnedAddress.cs          NEW  adds Address_* columns to BankAccounts
  Controllers/
    AccountDtos.cs                     + AddressDto; Address added to request/response
    AccountsController.cs              injects IAccountRepository; adds ?type filter
Lesson.Tests/
  AccountsControllerTests.cs          12 integration tests (was 7)
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~AccountsControllerTests"
# 12 tests — all pass
```

| Test | What it verifies |
|---|---|
| `GetAll_ReturnsSeededAccounts` | List returns at least the 2 seeded rows |
| `GetById_ExistingId_ReturnsAccount` | Correct entity for seed id=1 |
| `GetById_MissingId_ReturnsNotFound` | 404 for unknown id |
| `Create_ValidRequest_ReturnsCreated` | 201 + Location header + populated id |
| `Create_DuplicateAccountNumber_ReturnsConflict` | 409 on duplicate account number |
| `Update_ExistingAccount_ReturnsUpdatedData` | 200 + updated fields |
| `Delete_ExistingAccount_ReturnsNoContent` | 204 + subsequent GET = 404 |
| `GetAll_FilterByType_ReturnsOnlyMatchingAccounts` | `?type=Savings` excludes Checking rows |
| `GetAll_FilterByType_UnknownType_ReturnsEmptyList` | `?type=Unknown` returns `[]` |
| `Create_WithAddress_AddressRoundTrips` | Address persisted and returned in response |
| `Update_WithAddress_AddressIsPersisted` | Address can be set via PUT |
| `Create_WithoutAddress_AddressIsNull` | `address` is null when not provided |

---

## Exercises

1. Add `GET /accounts?minBalance=1000` — compose a second `IQueryable` filter for balance.
2. Expose `GET /accounts/by-number/{accountNumber}` using `SingleOrDefaultAsync` in the repository.
3. Add an `IAddressRepository` just for address lookups to explore repository decomposition.
4. Replace `OwnsOne` with `OwnsMany` and give each `BankAccount` a list of `PhoneNumber` value objects.

