using Lesson.DTOs;
using Lesson.Models;

namespace Lesson.Services;

// -----------------------------------------------------------------------------
// C# NOTE: This class is registered as a Singleton (see Program.cs), meaning
// the same instance — and therefore the same _accounts dictionary — is shared
// across all requests. This is fine for an in-memory store used in lessons.
//
// In Lesson 03 we replace this with a real database via EF Core.
//
// Java parallel:
//   @Service public class AccountServiceImpl implements AccountService { ... }
//   → public class AccountService : IAccountService { ... }
//
// C# uses ": InterfaceName" instead of "implements InterfaceName".
// Multiple interfaces are separated by commas: "class Foo : IBar, IBaz { ... }"
// -----------------------------------------------------------------------------

public class AccountService : IAccountService
{
    // Dictionary<TKey, TValue> is the C# equivalent of Java's HashMap<K, V>.
    // Using a private readonly field ensures it cannot be reassigned after construction.
    private readonly Dictionary<Guid, Account> _accounts = new();

    public AccountService()
    {
        // Seed two accounts so the API returns data immediately on first run.
        var alice = new Account(Guid.NewGuid(), "Alice Martin", "FR7630006000011234567890189", 12_500.00m, "EUR");
        var bob   = new Account(Guid.NewGuid(), "Bob Dupont",   "DE89370400440532013000",     4_200.50m,  "EUR");

        // C# NOTE: The "m" suffix denotes a decimal literal.
        //          Use decimal (not double) for monetary values — it avoids
        //          floating-point precision issues.
        //          Java equivalent: new BigDecimal("12500.00")

        _accounts[alice.Id] = alice;
        _accounts[bob.Id]   = bob;
    }

    public IEnumerable<AccountResponse> GetAll()
    {
        // LINQ: .Values gives all dictionary values; Select projects each Account
        // into an AccountResponse.  Covered in depth in Lesson 05.
        return _accounts.Values.Select(ToResponse);
    }

    public AccountResponse? GetById(Guid id)
    {
        // C# NOTE: The "?" on the return type means the method can return null.
        // This is a "nullable reference type" (enabled in the .csproj).
        // Java equivalent: Optional<AccountResponse>
        //
        // TryGetValue is the idiomatic Dictionary lookup — avoids double hashing
        // compared to ContainsKey + indexer.
        return _accounts.TryGetValue(id, out var account)
            ? ToResponse(account)
            : null;
    }

    public AccountResponse Create(CreateAccountRequest request)
    {
        var account = new Account(
            Id:       Guid.NewGuid(),
            Owner:    request.Owner,
            Iban:     request.Iban,
            Balance:  request.InitialBalance,
            Currency: request.Currency
        );

        _accounts[account.Id] = account;
        return ToResponse(account);
    }

    // -----------------------------------------------------------------------------
    // C# NOTE: Private helper method that maps a domain model to a response DTO.
    // This is the manual mapping approach.  Libraries like AutoMapper or Mapster
    // can automate this — similar to MapStruct in Java.
    // -----------------------------------------------------------------------------
    private static AccountResponse ToResponse(Account account) =>
        new(account.Id, account.Owner, account.Iban, account.Balance, account.Currency);
}
