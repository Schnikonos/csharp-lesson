using Lesson.DTOs;

namespace Lesson.Services;

// -----------------------------------------------------------------------------
// C# NOTE: Interfaces in C# are the standard way to define a service contract
// for Dependency Injection. The pattern is identical to Java's approach.
//
// Java parallel:
//   public interface AccountService { ... }    (Spring @Service contract)
//   → public interface IAccountService { ... } (C# convention: prefix with "I")
//
// The "I" prefix is a strong .NET convention (enforced by most style guides).
// Your concrete class will be AccountService : IAccountService.
//
// DI LIFETIMES (preview — covered in depth in Lesson 02):
//   Transient  → new instance every time it is requested
//   Scoped     → one instance per HTTP request  (= Spring's default @Service scope)
//   Singleton  → one instance for the app lifetime
// -----------------------------------------------------------------------------

public interface IAccountService
{
    IEnumerable<AccountResponse> GetAll();
    AccountResponse? GetById(Guid id);
    AccountResponse Create(CreateAccountRequest request);
}
