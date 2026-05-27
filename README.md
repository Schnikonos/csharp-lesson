# Lesson 01-A — Endpoint + Service + DI (Basic)

**Branch:** `lesson/01-endpoint/a-basic`  
**Level:** Basic  
**Next:** `lesson/01-endpoint/b-intermediate` (typed HttpClient, async, IOptions)

---

## What You Will Learn

| C# / ASP.NET Core | Java / Spring Boot equivalent |
|---|---|
| `[ApiController]` + `ControllerBase` | `@RestController` |
| `[Route("[controller]")]` | `@RequestMapping("/accounts")` |
| `[HttpGet]`, `[HttpPost]` | `@GetMapping`, `@PostMapping` |
| `IActionResult` / `ActionResult<T>` | `ResponseEntity<T>` |
| `record` type for DTOs | Java 16 `record` / Lombok `@Value` |
| Data Annotations `[Required]`, `[Range]` | Bean Validation `@NotNull`, `@Min` |
| `IAccountService` interface + DI | `AccountService` interface + `@Service` |
| `builder.Services.AddSingleton<I, T>()` | `@Bean` / `@ComponentScan` |
| `Guid` | `UUID` |
| `decimal` | `BigDecimal` |
| Nullable reference types (`T?`) | `Optional<T>` |

---

## Project Structure (this branch)

```
Lesson/
├── Controllers/
│   └── AccountController.cs    ← REST controller (3 endpoints)
├── DTOs/
│   ├── AccountResponse.cs      ← output DTO (record)
│   └── CreateAccountRequest.cs ← input DTO with validation attributes
├── Models/
│   └── Account.cs              ← domain model (record, in-memory)
├── Services/
│   ├── IAccountService.cs      ← service interface
│   └── AccountService.cs       ← in-memory implementation (seeded)
└── Program.cs                  ← DI registration + pipeline setup
```

---

## Endpoints

| Method | URL | Description |
|--------|-----|-------------|
| `GET` | `/account` | Returns all accounts |
| `GET` | `/account/{id}` | Returns one account by GUID, 404 if not found |
| `POST` | `/account` | Creates a new account, returns 201 + Location header |

---

## Running the Project

```bash
# From the solution root
dotnet run --project Lesson

# Interactive OpenAPI UI (Scalar) — opens automatically in development
# Navigate to: https://localhost:{port}/openapi/v1.json
```

---

## Key Concepts to Study

### 1. The [ApiController] attribute
Enables automatic model validation. If your `CreateAccountRequest` fails
validation (e.g. empty Owner), ASP.NET Core returns a `400 Bad Request` with
a structured `ValidationProblemDetails` body — **no manual `if (!ModelState.IsValid)` check needed**.

### 2. ActionResult\<T\> vs IActionResult
```csharp
// Option A — ActionResult<T>: return T directly OR wrap in a status-code helper
public ActionResult<AccountResponse> Create(...)
{
    return CreatedAtAction(...);  // or: return Ok(dto); or: return dto;
}

// Option B — IActionResult: full flexibility, no T constraint
public IActionResult GetById(Guid id)
{
    if (account is null) return NotFound();
    return Ok(account);
}
```

### 3. DI Lifetimes
```csharp
builder.Services.AddSingleton<IAccountService, AccountService>();  // one instance ever
builder.Services.AddScoped<IAccountService, AccountService>();     // one per HTTP request ← usual for services
builder.Services.AddTransient<IAccountService, AccountService>();  // new every time resolved
```
Switch the lifetime in `Program.cs` and observe behaviour (the in-memory list will behave differently).

### 4. C# record
```csharp
// Immutable, value-based equality, generated ToString()
public record Account(Guid Id, string Owner, string Iban, decimal Balance, string Currency);

// Positional construction
var a = new Account(Guid.NewGuid(), "Alice", "FR76...", 1000m, "EUR");

// Non-destructive mutation — creates a new instance with one field changed
var updated = a with { Balance = 2000m };
```

### 5. Nullable Reference Types
The project has `<Nullable>enable</Nullable>` in the `.csproj`.  
`AccountResponse?` means the method **may return null** — the compiler warns if
you dereference it without a null check. This is the C# equivalent of `Optional<T>`
but enforced at compile time rather than at runtime.

---

## Exercises

1. **Add `DELETE /account/{id}`** — return `204 No Content` on success, `404` if not found.
2. **Add `PUT /account/{id}`** — update the balance; think about what request DTO you need.
3. **Change the DI lifetime** from `Singleton` to `Scoped`. What breaks and why?
4. **Add a `MinimumBalance` validation** — balance must be at least 0.01 for EUR accounts.
5. **Return `409 Conflict`** if an account with the same IBAN already exists.

---

## Next Part

```bash
git checkout lesson/01-endpoint/b-intermediate

# See what was added
git diff lesson/01-endpoint/a-basic..lesson/01-endpoint/b-intermediate
```

Topics in **01-B**: typed `HttpClient`, `IHttpClientFactory`, `async`/`await`,
`CancellationToken`, `IOptions<T>` for configuration.
