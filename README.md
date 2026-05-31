# Lesson 10-B — IFormFile, CsvHelper & System.Text.Json Async IO

> **Branch:** `lesson/10-file-handling/b-intermediate`
> **Prerequisites:** Lesson 10-A (File, StreamReader/StreamWriter, FileStream)

---

## What you will learn

| Topic | C# .NET | Java parallel |
|---|---|---|
| `IFormFile` | ASP.NET Core multipart upload abstraction | `@RequestParam MultipartFile file` |
| `[Consumes("multipart/form-data")]` | document accepted content types | `@PostMapping(consumes = MediaType.MULTIPART_FORM_DATA_VALUE)` |
| `CsvHelper` | third-party CSV reader/writer | OpenCSV / Apache Commons CSV |
| `CsvReader.GetRecordsAsync<T>` | async streaming CSV parse | — |
| `[Name]` attribute | map CSV column by name | `@CsvBindByName` (OpenCSV) |
| `JsonSerializer.SerializeAsync` | async JSON ? stream | `ObjectMapper.writeValue(OutputStream, …)` |
| `JsonSerializer.DeserializeAsync` | async stream ? JSON | `ObjectMapper.readValue(InputStream, …)` |

---

## 1. IFormFile — file upload

```csharp
[HttpPost("import")]
[Consumes("multipart/form-data")]
public async Task<IActionResult> ImportCsv(IFormFile file, CancellationToken ct)
{
    using var reader = new StreamReader(file.OpenReadStream());
    // …
}
```

`IFormFile.OpenReadStream()` gives the raw byte stream without saving to disk.
Key properties: `FileName`, `Length`, `ContentType`, `OpenReadStream()`.

**Java parallel:**
```java
@PostMapping(consumes = MediaType.MULTIPART_FORM_DATA_VALUE)
public ResponseEntity<?> upload(@RequestParam("file") MultipartFile file) {
    try (var is = file.getInputStream()) { … }
}
```

---

## 2. CsvHelper — parsing CSV

```csharp
// Map columns by name using [Name] attribute on the record class
public record TransactionCsvRecord
{
    [Name("account_id")] public string AccountId { get; init; } = "";
    [Name("amount")]     public decimal Amount   { get; init; }
    // …
}

var config = new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true };
using var csv = new CsvReader(reader, config);

await foreach (var record in csv.GetRecordsAsync<TransactionCsvRecord>(ct))
    records.Add(record);
```

`GetRecordsAsync` returns an `IAsyncEnumerable<T>` — you can process each row without loading the full file into memory first.

---

## 3. System.Text.Json — async file IO

```csharp
// Write
await using var fs = new FileStream(path, FileMode.Create, …, useAsync: true);
await JsonSerializer.SerializeAsync(fs, payload, cancellationToken: ct);

// Read
await using var fs = new FileStream(path, FileMode.Open, …, useAsync: true);
var doc = await JsonSerializer.DeserializeAsync<JsonElement>(fs, cancellationToken: ct);
```

Both operations write/read directly from/to a `Stream` — no intermediate string allocation.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `POST` | `/files/csv/import` | Upload a CSV file (`multipart/form-data`), returns parsed records |
| `GET` | `/files/csv/export` | Download a CSV file (`text/csv`) |
| `POST` | `/files/json/save` | Serialize a JSON body to a temp file |
| `GET` | `/files/json/load?path=…` | Deserialize a JSON file |

---

## Project Structure (new / changed files)

```
Lesson/
  FileHandling/
    TransactionCsvRecord.cs  NEW  CsvHelper class map with [Name] attributes
  Controllers/
    CsvFileController.cs     NEW  /files/csv/* and /files/json/* endpoints
Lesson.Tests/
  CsvFileTests.cs            NEW  8 integration tests + CsvTestFactory
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~CsvFileTests"
# 8 tests — all pass
```

---

## Exercises

1. Add a `[Required]` column validation: if `amount` is missing return `400` instead of silently defaulting to `0`.
2. Write a CSV export endpoint that streams directly to the response body using `Response.Body` — no `MemoryStream` intermediate.
3. Use `JsonSerializerOptions` with `PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower` and observe how the saved JSON changes.
4. Add a `POST /files/csv/export` that accepts a list of records as JSON body and returns a CSV download.


> **Branch:** `lesson/10-file-handling/a-basic`
> **Prerequisites:** Lesson 09 (scheduled tasks)

---

# Lesson 26-C — Security Hardening

> **Branch:** `lesson/26-frontend-security/c-advanced`
> **Prerequisites:** Lesson 26-B (Razor Pages)

This lesson hardens the application against the most common browser-based attacks:
clickjacking, MIME sniffing, XSS, CSRF, cookie theft, and brute-force login.

## What you will learn

| Topic | C# .NET | Java / Spring Security parallel |
|---|---|---|
| Content-Security-Policy | `Content-Security-Policy` header via middleware | `http.headers().contentSecurityPolicy(...)` |
| Clickjacking | `X-Frame-Options: DENY` | `http.headers().frameOptions().deny()` |
| MIME sniffing | `X-Content-Type-Options: nosniff` | `http.headers().contentTypeOptions()` |
| Referrer leakage | `Referrer-Policy: strict-origin-when-cross-origin` | `http.headers().referrerPolicy(...)` |
| Cookie security | `HttpOnly`, `Secure`, `SameSite=Strict` on `CookieOptions` | `SessionCreationPolicy` + `CookieCsrfTokenRepository` |
| Anti-forgery (API) | `IAntiforgery.GetAndStoreTokens()` + `X-CSRF-TOKEN` header | Spring Security `CsrfTokenRepository` |
| Rate limiting | `AddRateLimiter` + `[EnableRateLimiting("login")]` fixed-window | Resilience4j `@RateLimiter` / bucket4j |
| XSS prevention | Razor `@Model.Value` auto-encodes; only `@Html.Raw` opts out | Thymeleaf auto-escapes by default |

## Middleware order (additions to 26-A/B)

```
SecurityHeadersMiddleware   ? CSP, X-Frame-Options, X-Content-Type-Options, Referrer-Policy
UseCookiePolicy             ? DefaultHttpOnly, SameSite defaults
ResponseHeaderMiddleware
RequestLoggingMiddleware
UseExceptionHandler
UseHttpsRedirection
UseHsts
UseCors
UseDefaultFiles
UseStaticFiles
UseAuthorization
UseRateLimiter              ? must come after UseAuthorization
MapControllers
MapRazorPages
MapFallbackToFile
```

## Key concepts

### SecurityHeadersMiddleware

```csharp
// Java: http.headers().contentSecurityPolicy("default-src 'self'")
headers["Content-Security-Policy"] = "default-src 'self'; ...";
headers["X-Frame-Options"]         = "DENY";
headers["X-Content-Type-Options"]  = "nosniff";
headers["Referrer-Policy"]         = "strict-origin-when-cross-origin";
```

### Cookie flags

```csharp
// Java: new Cookie("session", value).setHttpOnly(true).setSecure(true).setSameSite("Strict")
Response.Cookies.Append("session", value, new CookieOptions
{
    HttpOnly = true,
    Secure   = true,
    SameSite = SameSiteMode.Strict
});
```

### Anti-forgery (IAntiforgery)

```csharp
// GET /api/login/token — returns token for SPA to include in X-CSRF-TOKEN header
var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
return Ok(new { token = tokens.RequestToken, headerName = tokens.HeaderName });
```

### Rate limiting

```csharp
// Registration
builder.Services.AddRateLimiter(options =>
    options.AddFixedWindowLimiter("login", cfg => {
        cfg.PermitLimit = 5;
        cfg.Window = TimeSpan.FromMinutes(1);
    }));

// Usage — on the action method
[EnableRateLimiting("login")]
public IActionResult Login(...) { ... }
```

## Project structure (new / changed files)

```
Lesson/
  Middleware/
    SecurityHeadersMiddleware.cs  NEW  CSP + X-Frame-Options + X-Content-Type-Options + Referrer-Policy
  Controllers/
    LoginController.cs            NEW  GET /api/login/token, POST /api/login with [EnableRateLimiting]
  Program.cs                      MOD  SecurityHeadersMiddleware, cookie policy, IAntiforgery config,
                                       AddRateLimiter + UseRateLimiter
Lesson.Tests/
  SecurityHeadersTests.cs         NEW  7 tests (headers, cookie flags, anti-forgery, XSS encoding)
  (RateLimitingTests class)        NEW  1 test (429 on 6th request)
```

## Tests

```bash
dotnet test --filter "FullyQualifiedName~SecurityHeadersTests|FullyQualifiedName~RateLimitingTests"
# 8 tests — all pass
```

## Exercises

1. Add a `Permissions-Policy` response header disabling camera and microphone: `camera=(), microphone=()`.
2. Change the anti-forgery cookie to `HttpOnly = true` and update the SPA to read the token from the response body instead.
3. Switch the rate limiter from fixed-window to a **sliding-window** policy and observe the difference in behaviour under burst load.
4. Add a `[ValidateAntiForgeryToken]` attribute to a controller action and write a test verifying the token must be present.

---

> **Branch:** `lesson/26-frontend-security/b-intermediate`
> **Prerequisites:** Lesson 26-A (static files, CORS)

Razor Pages is ASP.NET Core's server-side HTML rendering model — the direct .NET equivalent
of Thymeleaf (Spring Boot) or Jinja2 (Flask). Each page is a `.cshtml` template bound to a
`PageModel` code-behind class.

## What you will learn

| Razor concept | C# .NET | Java / Thymeleaf parallel |
|---|---|---|
| Page declaration | `@page` directive | `@Controller` + `@GetMapping` |
| Code-behind | `PageModel` with `OnGet` / `OnPost` | `@Controller` / `@ModelAttribute` |
| Data binding | `@Model.CustomerName` | `${customer.name}` / `th:text="${customer.name}"` |
| Layout | `Pages/Shared/_Layout.cshtml` + `_ViewStart.cshtml` | `th:layout:decorate` / Thymeleaf Layout Dialect |
| Partial views | `<partial name="_TransactionRow" model="tx" />` | `th:replace` / `th:insert` fragments |
| Tag helpers | `<a asp-page="Statement">` | `th:href="@{/statement}"` |
| Anti-forgery | `@Html.AntiForgeryToken()` in form | Spring Security CSRF token in `<form>` |
| `@inject` | Inject a service directly into `.cshtml` | `@Autowired` + Thymeleaf dialect bean |
| Auto-encoding | `@Model.CustomerName` is HTML-encoded | Thymeleaf auto-escapes by default |

## Auto-encoding (XSS prevention preview)

Razor always HTML-encodes output by default:

```cshtml
@Model.CustomerName   @* safe — "<script>" becomes "&lt;script&gt;" *@
@Html.Raw(...)        @* explicit opt-out — only for trusted content *@
```

This is the foundation of XSS prevention developed further in Lesson 26-C.

## Project structure (new / changed files)

```
Lesson/
  Pages/
    _ViewStart.cshtml         NEW  sets Layout = "_Layout" for all pages
    _ViewImports.cshtml       NEW  imports tag helper assemblies
    Shared/
      _Layout.cshtml          NEW  master layout (nav + footer)
      _TransactionRow.cshtml  NEW  partial view for one transaction row
    Statement.cshtml          NEW  bank statement Razor Page (@page)
    Statement.cshtml.cs       NEW  StatementModel (OnGet, OnPost)
  Program.cs                  MOD  AddRazorPages(), MapRazorPages()
Lesson.Tests/
  RazorPagesTests.cs          NEW  6 integration tests
```

## Tests

```bash
dotnet test --filter "FullyQualifiedName~RazorPagesTests"
# 6 tests — all pass
```

## Exercises

1. Add a second Razor Page `Pages/Transfer.cshtml` with a deposit form that POSTs back and shows a success message using `TempData`.
2. Add a `@inject ILogger<StatementModel> Logger` directly in the `.cshtml` file and log a debug message when the page renders.
3. Create a `_SummaryCard.cshtml` partial view that shows the opening/closing balance in a styled box and include it in `Statement.cshtml`.
4. Add server-side model validation: if `accountNumber` is empty, add a `ModelState` error and redisplay the form.

---

> **Branch:** `lesson/26-frontend-security/a-basic`
> **Prerequisites:** Lesson 10-C (templating) or any Lesson with a working API

This lesson teaches how to serve a frontend (static files, SPA) and lock down cross-origin
requests and transport security — the minimum "production checklist" for any web API.

## What you will learn

| Topic | C# .NET | Java parallel |
|---|---|---|
| Static file serving | `UseStaticFiles()` + `wwwroot/` | `spring.web.resources.static-locations` + `ResourceHttpRequestHandler` |
| Default document | `UseDefaultFiles()` rewrites `/` ? `/index.html` | `ResourceHttpRequestHandler` welcome-file list |
| SPA fallback | `MapFallbackToFile("index.html")` | Custom `SpaFallbackRequestMatcher` / catch-all `@RequestMapping` |
| CORS policy | `AddCors` + `AllowSpecificOrigins` policy | `@CrossOrigin` / `WebMvcConfigurer.addCorsMappings()` |
| CORS preflight | Automatic `OPTIONS` handling with `Access-Control-Allow-*` headers | Spring Security `CorsFilter` |
| HTTPS redirect | `UseHttpsRedirection()` | `http.requiresChannel().anyRequest().requiresSecure()` |
| HSTS | `UseHsts()` (production only) | `http.headers().httpStrictTransportSecurity()` |

## Middleware order (critical)

```
UseHttpsRedirection
UseHsts              (prod only)
UseCors              ? must precede UseStaticFiles and MapControllers
UseDefaultFiles      ? must precede UseStaticFiles
UseStaticFiles
UseAuthorization
MapControllers
MapFallbackToFile    ? must come AFTER MapControllers
```

## Key concepts

### CORS policy

```csharp
// Registration (services)
builder.Services.AddCors(options =>
    options.AddPolicy("AllowSpecificOrigins", policy =>
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()));

// Activation (middleware, before static files)
app.UseCors("AllowSpecificOrigins");
```

### SPA fallback

```csharp
// After MapControllers so API routes take priority
app.MapFallbackToFile("index.html");
```
Unknown paths (e.g. `/accounts/dashboard`) return `200 + index.html` so the SPA router
handles client-side navigation — not a 404.

### HSTS (HTTP Strict Transport Security)

Only active outside Development. Browsers will refuse to connect over HTTP for the
`max-age` duration after the first HTTPS response.

## Project structure (new / changed files)

```
Lesson/
  wwwroot/
    index.html          NEW  SPA shell — served by UseDefaultFiles + UseStaticFiles
    css/app.css         NEW  static stylesheet
    js/app.js           NEW  static script
  Program.cs            MOD  AddCors, UseCors, UseDefaultFiles, UseStaticFiles,
                             UseHsts, MapFallbackToFile
Lesson.Tests/
  StaticFilesCorsSpaTests.cs   NEW  8 integration tests
```

## Tests

```bash
dotnet test --filter "FullyQualifiedName~StaticFilesCorsSpaTests"
# 8 tests — all pass
```

## Exercises

1. Add a second CORS policy `"AllowAll"` with `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()` and apply it only to a `/api/public/**` group.
2. Change `MapFallbackToFile` to return `404` for paths starting with `/api/` to prevent API misroutes from silently returning HTML.
3. Add a `Cache-Control: public, max-age=31536000, immutable` header to versioned static assets (e.g. `*.min.js`) using `StaticFileOptions`.
4. Enable HSTS in Development by moving `UseHsts` outside the `if (!IsDevelopment())` guard — observe the browser behaviour and understand why it defaults to prod-only.

---

> **Branch:** `lesson/10-file-handling/c-advanced`
> **Prerequisites:** Lesson 10-B (file I/O, CsvHelper)

Scriban is a lightweight .NET template engine with `{{ variable }}` syntax inspired by Liquid and Jinja2.
Use it wherever you need to generate text artefacts **outside** of a web request — transaction emails,
PDF/text reports, bank statements, batch files, or any other text-based output.

## What you will learn

| Topic | C# .NET | Java parallel |
|---|---|---|
| Template engine abstraction | `ITemplateEngine` / `ScribanTemplateEngine` | `TemplateEngine` (Thymeleaf) / `Environment` (Jinja2) |
| Variable interpolation | `{{ customer_name }}` | `${customerName}` / `th:text="${customer}"` |
| Loops | `{{- for tx in transactions }} … {{- end }}` | `th:each` / `{% for %}` |
| Conditionals | `{{ if is_vip }} … {{ else }} … {{ end }}` | `th:if` / `{% if %}` |
| Filters / pipes | `{{ amount \| math.format "0.00" }}` | `#numbers.formatDecimal` / `{{ amount\|format }}` |
| Model binding | PascalCase ? `snake_case` via custom renamer | `th:object="${model}"` / Jinja2 context dict |
| File-based templates | `RenderAsync("bank-statement.txt", model)` | `templateEngine.process("statement", context)` |
| Inline templates | `RenderStringAsync(source, model)` | `templateEngine.process(new StringTemplateResource(s), ctx)` |

## Key concepts

### `ITemplateEngine` — the abstraction

```csharp
// Java: TemplateEngine / Environment.get_template(name).render(context)
public interface ITemplateEngine
{
    Task<string> RenderAsync(string templateName, object model, CancellationToken ct = default);
    Task<string> RenderStringAsync(string templateSource, object model, CancellationToken ct = default);
}
```

Registered as a **singleton** because the Scriban parse tree is thread-safe and stateless:

```csharp
builder.Services.AddSingleton<ITemplateEngine>(_ =>
    new ScribanTemplateEngine(
        Path.Combine(AppContext.BaseDirectory, "Templating", "Templates")));
```

### PascalCase ? snake_case renaming

Scriban expects `{{ transaction_id }}` (snake_case). The engine automatically converts
.NET PascalCase property names using a regex renamer — no manual mapping required.

### Template file location

Templates live in `Lesson/Templating/Templates/` and are copied to the output directory
via the `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>` csproj directive.
The same copy rule applies to the test project so file-based rendering tests work without
a running web server.

## Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/api/templating/email/{transactionId}` | Renders a transaction confirmation email |
| `GET` | `/api/templating/statement/{accountNumber}` | Renders a plain-text bank statement |
| `GET` | `/api/templating/report/{year}/{month}` | Renders a monthly activity report |
| `POST` | `/api/templating/inline` | Renders an arbitrary inline Scriban template |

## Project structure (new / changed files)

```
Lesson/
  Templating/
    ITemplateEngine.cs          NEW  abstraction (like Thymeleaf TemplateEngine interface)
    ScribanTemplateEngine.cs    NEW  Scriban implementation with PascalCase?snake_case renamer
    Templates/
      transaction-email.txt     NEW  email body template
      bank-statement.txt        NEW  plain-text statement template
      monthly-report.txt        NEW  monthly report template
  Controllers/
    TemplatingController.cs     NEW  render endpoints
  Program.cs                    MOD  ITemplateEngine singleton registration
  Lesson.csproj                 MOD  Scriban package + CopyToOutputDirectory for templates
Lesson.Tests/
  TemplatingTests.cs            NEW  9 tests covering RenderStringAsync + RenderAsync
  Lesson.Tests.csproj           MOD  CopyToOutputDirectory for template files
```

## Tests

```bash
dotnet test --filter "FullyQualifiedName~TemplatingTests"
# 9 tests — all pass
```

## Exercises

1. Add a `{{ if amount > 1000 }}HIGH VALUE{{ end }}` badge to `transaction-email.txt` and write a test for it.
2. Create a new `welcome-letter.txt` template and a `/api/templating/welcome/{customerId}` endpoint.
3. Extend `ScribanTemplateEngine` to support template **caching** — parse each template once and reuse the AST.
4. Render a Scriban template to HTML and return it as `text/html` — observe how `{{ }}` prevents XSS by auto-encoding (compare with Razor's `@Html.Raw`).

---

## What you will learn

| Topic | C# .NET | Java parallel |
|---|---|---|
| Static helpers | `File.Exists`, `File.Delete`, `File.AppendText`, `new FileInfo(path)` | `java.nio.file.Files`, `new File(path)` |
| `StreamWriter` | `new StreamWriter(path)`, `await using` | `PrintWriter`/`BufferedWriter` via `Files.newBufferedWriter` |
| `StreamReader` | `new StreamReader(path)`, `ReadLineAsync` | `BufferedReader` via `Files.newBufferedReader` |
| `FileStream` | low-level byte stream; explicit mode, access, share, buffer flags | `FileOutputStream` + `BufferedOutputStream` |
| `using` declaration | C# 8+ — disposes at end of enclosing scope; no extra braces needed | `try`-with-resources |
| `await using` | `IAsyncDisposable` — flushes buffers asynchronously before dispose | No direct equivalent |

---

## 1. StreamWriter — writing text

```csharp
// 'await using' flushes asynchronously; works because StreamWriter : IAsyncDisposable
await using var writer = new StreamWriter(path, append: false);
await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
```

**`append: false`** — truncates and creates; `append: true` — opens or creates and seeks to end.

**Java parallel:**
```java
try (var writer = Files.newBufferedWriter(Path.of(path))) {
    writer.write(line);
    writer.newLine();
}
```

---

## 2. StreamReader — reading text

```csharp
// StreamReader does not implement IAsyncDisposable ? plain 'using'
using var reader = new StreamReader(path);
while (await reader.ReadLineAsync(ct) is { } line)
    lines.Add(line);
```

`ReadLineAsync` returns `null` at EOF — the `is { }` pattern filters nulls out cleanly.

---

## 3. FileStream — binary data

```csharp
await using var fs = new FileStream(
    path,
    FileMode.Create,       // create or overwrite
    FileAccess.Write,
    FileShare.None,        // no concurrent readers/writers
    bufferSize: 4096,
    useAsync: true);       // enables OS async I/O on Windows

await fs.WriteAsync(bytes, cancellationToken);
```

`useAsync: true` is important for `await fs.WriteAsync` to be truly asynchronous on Windows.

---

## 4. File / FileInfo static helpers

```csharp
File.Exists(path)              // bool
File.Delete(path)              // void — throws if missing
File.AppendText(path)          // StreamWriter opened for append
new FileInfo(path).Length      // file size in bytes
new FileInfo(path).LastWriteTimeUtc
```

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `POST` | `/files/export` | Write transaction list to a temp text file |
| `GET` | `/files/read?path=…` | Read all lines from a file |
| `POST` | `/files/append` | Append a single line to an existing file |
| `GET` | `/files/info?path=…` | File metadata (size, dates) |
| `DELETE` | `/files/delete?path=…` | Remove a file |
| `POST` | `/files/binary` | Write Base64-encoded bytes via `FileStream` |

---

## Project Structure (new / changed files)

```
Lesson/
  FileHandling/
    FileHandlingDtos.cs          NEW  Request record types
  Controllers/
    FileHandlingController.cs    NEW  /files/* endpoints
Lesson.Tests/
  FileHandlingBasicTests.cs      NEW  8 integration tests + FileHandlingTestFactory
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~FileHandlingBasicTests"
# 8 tests — all pass
```

---

## Exercises

1. Add a `GET /files/list?directory=…` endpoint that returns all `.txt` files in a directory using `Directory.EnumerateFiles`.
2. Change `WriteBinary` to use `await File.WriteAllBytesAsync(path, bytes, ct)` — compare the two approaches.
3. Read a large file using `ReadAllLinesAsync` vs the `StreamReader` loop — benchmark with `Stopwatch` to understand the tradeoff.
4. Add compression: wrap `FileStream` in a `GZipStream` (`System.IO.Compression`) and write compressed text.


> **Branch:** `lesson/09-scheduled-tasks/b-intermediate`
> **Prerequisites:** Lesson 09-A (PeriodicTimer, BackgroundService)

---

## What you will learn

| Topic | Quartz.NET | Java parallel |
|---|---|---|
| `IJob` | unit of work executed by the scheduler | `org.quartz.Job` |
| `IJobDetail` | metadata + data map for a job class | `JobDetail` (Quartz for Java) |
| `ITrigger` / cron trigger | when a job fires | `CronTrigger` |
| `IScheduler` | engine that manages triggers + executes jobs | `org.quartz.Scheduler` |
| `[DisallowConcurrentExecution]` | prevents overlapping executions | `@DisallowConcurrentExecution` |
| DI-integrated jobs | jobs resolved from `IServiceProvider` per execution — can receive scoped services | Spring Quartz `AutowireCapableBeanJobFactory` |
| `ISchedulerFactory.GetScheduler()` | obtain the running scheduler | `SchedulerFactory.getScheduler()` |
| Manual trigger | `scheduler.TriggerJob(key)` — fire on demand | `scheduler.triggerJob(key)` |

---

## 1. Defining an IJob

```csharp
[DisallowConcurrentExecution]
public class StatementGenerationJob(
    JobHistoryStore store,
    ILogger<StatementGenerationJob> logger) : IJob
{
    public static readonly JobKey Key = new("StatementGeneration", "Banking");

    public async Task Execute(IJobExecutionContext context)
    {
        // context.FireTimeUtc — when the trigger fired
        // context.CancellationToken — cancelled on graceful shutdown
        await DoWorkAsync(context.CancellationToken);
    }
}
```

`[DisallowConcurrentExecution]` tells Quartz not to start a new execution until the previous one completes — equivalent to `@DisallowConcurrentExecution` in Java Quartz.

---

## 2. Registration in Program.cs

```csharp
builder.Services.AddQuartz(q =>
{
    q.AddJob<StatementGenerationJob>(opts => opts.WithIdentity(StatementGenerationJob.Key));

    q.AddTrigger(opts => opts
        .ForJob(StatementGenerationJob.Key)
        .WithIdentity("StatementTrigger", "Banking")
        .WithCronSchedule("0 * * * * ?")   // every minute
        .StartNow());
});

builder.Services.AddQuartzHostedService(opts =>
    opts.WaitForJobsToComplete = true);    // graceful shutdown
```

`AddQuartz` registers the Quartz scheduler as a DI service.
`AddQuartzHostedService` starts it as an `IHostedService`.

**Java parallel:** `spring-boot-starter-quartz` + `@Configuration` with `JobDetailFactoryBean` + `CronTriggerFactoryBean`.

---

## 3. Cron expressions

| Expression | Meaning |
|---|---|
| `0 * * * * ?` | Every minute at second 0 |
| `0 0 2 * * ?` | Every day at 02:00 |
| `0 0/15 * * * ?` | Every 15 minutes |
| `0 0 9-17 ? * MON-FRI` | Every hour 9 AM–5 PM, weekdays |

Quartz.NET cron has **6 fields** (seconds included), unlike the 5-field Unix cron.

---

## 4. Manual trigger (on-demand execution)

```csharp
var scheduler = await schedulerFactory.GetScheduler(ct);
await scheduler.TriggerJob(StatementGenerationJob.Key, ct);
```

Useful for: batch job dashboards, retry buttons, testing.

---

## PeriodicTimer vs Quartz

| | `PeriodicTimer` (09-A) | Quartz.NET (09-B) |
|---|---|---|
| Scheduling | Fixed period from last tick | Cron / calendars / misfire policies |
| Concurrency control | Manual | `[DisallowConcurrentExecution]` |
| Persistence | None (in-memory) | Optional (ADO.NET job store) |
| Monitoring | Custom | Quartz dashboard, `ISchedulerFactory` |
| Use case | Simple recurring task | Enterprise scheduling, complex calendars |

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `POST` | `/quartz/trigger` | Fires `StatementGenerationJob` immediately |
| `GET` | `/quartz/history` | Returns job execution history |
| `DELETE` | `/quartz/history/reset` | Clears history (test helper) |

---

## Project Structure (new / changed files)

```
Lesson/
  Jobs/
    StatementGenerationJob.cs    NEW  IJob implementation + JobKey
  Controllers/
    QuartzJobController.cs       NEW  /quartz/* endpoints
  Program.cs                          + AddQuartz, AddQuartzHostedService
Lesson.Tests/
  QuartzJobTests.cs              NEW  8 integration tests + QuartzTestFactory
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~QuartzJobTests"
# 8 tests — all pass
```

> **Testing note:** Quartz uses a static logger provider that captures `LoggerFactory`
> at startup. Tests use a `QuartzTestFactory : WebApplicationFactory<Program>` subclass
> with `ConfigureWebHost` override — this ensures only one host is created per test run,
> avoiding the `ObjectDisposedException` that occurs with per-test `WithWebHostBuilder` calls.

---

## Exercises

1. Add a `JobDataMap` to the trigger with a `reportType` key and read it from `IJobExecutionContext.MergedJobDataMap` inside the job.
2. Change the cron to `"0/5 * * * * ?"` (every 5 seconds), run the app, and observe multiple executions in the history endpoint.
3. Remove `[DisallowConcurrentExecution]` and trigger the job twice rapidly — observe both runs appearing in "Running" state simultaneously.
4. Add a second `IJob` (`AuditReportJob`) with its own trigger and verify both jobs fire independently.


> **Branch:** `lesson/09-scheduled-tasks/a-basic`
> **Prerequisites:** Lesson 08 (events, pub/sub, Channel\<T\>)

---

## What you will learn

| Topic | C# .NET | Java parallel |
|---|---|---|
| `IHostedService` | long-running service managed by the generic host | `ApplicationRunner` / `CommandLineRunner` |
| `BackgroundService` | abstract base — override `ExecuteAsync(CancellationToken)` | `ThreadPoolTaskExecutor` loop |
| `PeriodicTimer` | modern, alloc-free, non-blocking periodic tick | `@Scheduled(fixedDelay = …)` |
| `CancellationToken` | graceful shutdown signal from the host | `Thread.interrupt()` |
| Singleton + IHostedService | register same instance as both singleton and hosted service | Spring `@Component` with `@Scheduled` method |

---

## 1. PeriodicTimer — the modern scheduling primitive

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

    while (await timer.WaitForNextTickAsync(stoppingToken))
    {
        // runs once per period
        await DoWorkAsync(stoppingToken);
    }
}
```

Key properties of `PeriodicTimer`:
- `WaitForNextTickAsync` suspends **without blocking a thread** — much more efficient than `Thread.Sleep` or `Task.Delay` loops.
- **Tick skipping**: if `DoWorkAsync` takes longer than the period, the *next* tick fires immediately once. Subsequent ticks resume on schedule. This prevents work piling up.
- Returns `false` when `stoppingToken` is cancelled ? clean loop exit.

**Java parallel:**
```java
@Scheduled(fixedDelay = 30_000)
public void doWork() { … }
```
The key difference: Spring creates a new thread per invocation; `PeriodicTimer` reuses the same `async` continuation on the thread pool.

---

## 2. Registering the same instance as singleton + IHostedService

```csharp
// Register the concrete type as a singleton so other services (e.g. controller) can inject it
builder.Services.AddSingleton<InterestCalculationService>(sp =>
    new InterestCalculationService(
        sp.GetRequiredService<JobHistoryStore>(),
        sp.GetRequiredService<ILogger<InterestCalculationService>>(),
        period: TimeSpan.FromSeconds(30)));

// Tell the host to start/stop this *same* instance
builder.Services.AddHostedService(sp => sp.GetRequiredService<InterestCalculationService>());
```

Registering the concrete type as a singleton first allows `ScheduledTasksController` to inject `InterestCalculationService` directly and read its execution log.

---

## 3. JobHistoryStore

```csharp
public sealed class JobHistoryStore
{
    private readonly List<JobExecution> _history = [];
    public IReadOnlyList<JobExecution> History => _history.AsReadOnly();

    public void Add(JobExecution run) { … }
    public void Update(Guid runId, JobExecution updated) { … }
    public void Clear() { … }
}
```

Singleton in-memory store — shared between the background service (writer) and the controller (reader).

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/scheduled-tasks/history` | Returns all recorded job runs |
| `DELETE` | `/scheduled-tasks/history/reset` | Clears the history (test helper) |

---

## Project Structure (new / changed files)

```
Lesson/
  ScheduledTasks/
    JobExecution.cs               NEW  Record — execution snapshot
    JobHistoryStore.cs            NEW  Singleton in-memory execution log
  HostedServices/
    InterestCalculationService.cs NEW  BackgroundService + PeriodicTimer
  Controllers/
    ScheduledTasksController.cs   NEW  /scheduled-tasks/* endpoints
  Program.cs                          + JobHistoryStore, InterestCalculationService registrations
Lesson.Tests/
  ScheduledTaskBasicTests.cs      NEW  7 tests (6 integration + 1 unit)
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~ScheduledTaskBasicTests"
# 7 tests — all pass
```

| Test | What it verifies |
|---|---|
| `GetHistory_Returns200` | Endpoint responds |
| `GetHistory_ReturnsEmptyArrayByDefault` | Clean initial state |
| `Reset_Returns204` | Reset endpoint |
| `AddToStore_AppearsInHistoryEndpoint` | Store ? controller integration |
| `AfterReset_HistoryIsEmpty` | Reset clears the store |
| `MultipleRuns_AllAppearInHistory` | Multiple entries accumulate |
| `InterestCalculationService_ExecutesOneTickAndRecordsCompletedRun` | Service actually runs, job completes, history updated |

> **Testing note:** Background service timing is non-deterministic in the test host.
> Integration tests verify the HTTP API and DI wiring. The one unit test drives
> `ExecuteAsync` directly with a short period and polls for completion.

---

## Exercises

1. Add a `LastRun` property to `JobHistoryStore` and expose it on a `GET /scheduled-tasks/last-run` endpoint.
2. Change from `UnboundedChannel` to a bounded channel with capacity 1 — observe that the timer skips ticks while the job is running.
3. Inject `IServiceScopeFactory` into `InterestCalculationService` and open a scoped `BankingDbContext` inside the tick loop to read real account data.
4. Add a `[Fact]` that checks a tick still fires after `StopAsync` is called with a 5-second timeout — verifying graceful shutdown.


> **Branch:** `lesson/08-events/c-advanced`
> **Prerequisites:** Lesson 08-B (MediatR INotification, fan-out pub/sub)

---

## What you will learn

| Topic | C# .NET | Java parallel |
|---|---|---|
| `IHostedService` | long-running background service managed by the host | `@Component` + `ApplicationRunner` / `Runnable` in a thread pool |
| `BackgroundService` | abstract base class — override `ExecuteAsync(CancellationToken)` | `ThreadPoolTaskExecutor` task that loops until interrupted |
| `CancellationToken` | signals graceful shutdown | `Thread.interrupt()` / `InterruptedException` |
| `Channel<T>` | thread-safe, lock-free async queue | `LinkedBlockingQueue<T>` / `ArrayBlockingQueue<T>` |
| `ChannelWriter<T>` | producer side — `WriteAsync`, `TryWrite` | `queue.put()` |
| `ChannelReader<T>` | consumer side — `ReadAllAsync`, `ReadAsync` | `queue.take()` |
| Bounded vs Unbounded | `Channel.CreateBounded` / `Channel.CreateUnbounded` | `ArrayBlockingQueue(N)` / `LinkedBlockingQueue()` |
| Outbox pattern intro | publish message ? queue ? background consumer | Transactional outbox with Kafka/RabbitMQ |

---

## 1. BackgroundService

```csharp
public class OutboxConsumerService(OutboxChannel channel, ILogger<...> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in channel.Reader.ReadAllAsync(stoppingToken))
        {
            // process each message
            await Task.Delay(5, stoppingToken);
            _processed.Add(message);
        }
    }
}
```

`ExecuteAsync` runs on a background thread.  
`ReadAllAsync` suspends (without blocking a thread) until a message is available or the token is cancelled.  
When the application stops, `stoppingToken` is cancelled ? `ReadAllAsync` throws `OperationCanceledException` ? the `await foreach` exits cleanly.

**Java parallel:**
```java
@Bean
public ApplicationRunner consumer(BlockingQueue<Message> queue) {
    return args -> {
        while (!Thread.currentThread().isInterrupted()) {
            Message msg = queue.take();   // blocks thread
            process(msg);
        }
    };
}
```
The key difference: `ReadAllAsync` releases the thread while waiting; `BlockingQueue.take()` blocks it.

---

## 2. Channel\<T\>

```csharp
// Unbounded — producer never waits; ideal when producer is bursty and consumer is reliable
var channel = Channel.CreateUnbounded<OutboxMessage>(
    new UnboundedChannelOptions { SingleReader = true });

// Producer
await channel.Writer.WriteAsync(message);   // completes immediately (unbounded)

// Consumer
await foreach (var msg in channel.Reader.ReadAllAsync(ct))
    Process(msg);
```

`SingleReader = true` is a performance hint — allows internal optimisations when only one consumer exists.

---

## 3. Registration in Program.cs

```csharp
// Singleton — the channel is shared between producer (controller) and consumer (hosted service)
builder.Services.AddSingleton<OutboxChannel>();

// Register the concrete type so OutboxController can inject it directly to read Processed log
builder.Services.AddSingleton<OutboxConsumerService>();
// Register the same instance as IHostedService so the host starts/stops it
builder.Services.AddHostedService(sp => sp.GetRequiredService<OutboxConsumerService>());
```

Registering the singleton separately and then as `IHostedService` via factory ensures that
`OutboxController` and the host share the **same instance** of the consumer.

---

## 4. Outbox Pattern (conceptual)

In a real outbox pattern the flow is:
```
Controller ? write to DB outbox table (same transaction as the business change)
BackgroundService ? poll outbox table ? publish to message broker ? mark as processed
```
Here we replace the DB table with an in-memory `Channel<T>` to focus on the
`IHostedService` + `Channel<T>` mechanics without introducing an actual broker.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `POST` | `/outbox` | Writes message to `Channel<T>` ? 202 Accepted |
| `GET` | `/outbox/processed` | Returns messages already consumed by `OutboxConsumerService` |

---

## Project Structure (new / changed files)

```
Lesson/
  Messaging/
    OutboxMessage.cs          NEW  Record — channel message payload
    OutboxChannel.cs          NEW  Wraps Channel<OutboxMessage>; exposes Writer/Reader
  HostedServices/
    OutboxConsumerService.cs  NEW  BackgroundService — drains channel, simulates processing
  Controllers/
    OutboxController.cs       NEW  /outbox producer endpoints
  Program.cs                        + singleton channel, consumer, hosted service
Lesson.Tests/
  ChannelHostedServiceTests.cs  NEW  6 integration tests (with polling helper for async)
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~ChannelHostedServiceTests"
# 6 tests — all pass
```

| Test | What it verifies |
|---|---|
| `Publish_Returns202Accepted` | Producer endpoint returns 202 |
| `Publish_ResponseContainsMessageId` | Guid and queued flag present |
| `AfterPublish_MessageIsProcessedByBackgroundService` | Consumer receives the message |
| `AfterMultiplePublishes_AllMessagesProcessed` | All messages consumed in order |
| `ProcessedEntry_ContainsCorrectEventType` | Data integrity through the queue |
| `ProcessedEntry_PayloadIsSerializedJson` | Payload serialised as JSON |

> **Testing note:** Because the consumer is asynchronous, tests use a `WaitForProcessedAsync`
> polling helper that retries the `/outbox/processed` endpoint up to 2 seconds before failing.
> In production code, prefer awaitable completion signals over polling.

---

## Exercises

1. Switch from `UnboundedChannel` to `BoundedChannel` with capacity 3 and observe what happens when you publish 4 messages rapidly with `BoundedChannelFullMode.Wait`.
2. Add a `DeadLetterChannel` — if processing throws, write the failed message to a second `Channel<T>` and expose a `/outbox/dead-letter` endpoint.
3. Add a second `BackgroundService` that reads from the same channel — observe that with `SingleReader = true` you get a runtime exception, then switch to `SingleReader = false`.
4. Introduce a simulated transactional outbox: save the `OutboxMessage` to the SQLite `BankingDbContext` in the controller, and have the background service query the DB instead of reading from the channel.


> **Branch:** `lesson/08-events/b-intermediate`
> **Prerequisites:** Lesson 08-A (delegates, event keyword, in-process pub/sub)

---

## What you will learn

| Topic | C# / MediatR | Java parallel |
|---|---|---|
| `INotification` | marker interface for a domain event | `ApplicationEvent` subclass |
| `INotificationHandler<T>` | receives a notification; multiple per notification | `@EventListener` method |
| `IMediator.Publish()` | dispatch to all handlers | `ApplicationEventPublisher.publishEvent()` |
| Decoupling | publisher has zero dependency on handlers | `@EventListener` — no direct coupling either |
| Multiple handlers | fan-out — all handlers are called | Multiple `@EventListener` methods for the same event type |

---

## 1. INotification — the event payload

```csharp
public record AccountCreatedNotification(
    Guid AccountId,
    string OwnerName,
    decimal InitialBalance) : INotification;
```

`INotification` is a **marker interface** — no methods required.
MediatR uses the type to route the notification to the correct handlers.

**Java parallel:**
```java
public class AccountCreatedEvent extends ApplicationEvent {
    public AccountCreatedEvent(Object source, UUID accountId, String ownerName) { ... }
}
```

---

## 2. INotificationHandler — subscribers

```csharp
public class SendWelcomeEmailHandler : INotificationHandler<AccountCreatedNotification>
{
    public Task Handle(AccountCreatedNotification notification, CancellationToken ct)
    {
        // send welcome email (simulated)
        return Task.CompletedTask;
    }
}

public class AccountCreatedAuditHandler : INotificationHandler<AccountCreatedNotification>
{
    public Task Handle(AccountCreatedNotification notification, CancellationToken ct)
    {
        _log.Add(notification); // record in audit
        return Task.CompletedTask;
    }
}
```

Both handlers are called automatically when the notification is published.
Handlers are registered by `AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>())`.

**Java parallel:**
```java
@Component class WelcomeEmailListener {
    @EventListener AccountCreatedEvent handle(AccountCreatedEvent e) { ... }
}
@Component class AuditListener {
    @EventListener AccountCreatedEvent handle(AccountCreatedEvent e) { ... }
}
```

---

## 3. Publishing via IMediator

```csharp
[ApiController] [Route("accounts-events")]
public class AccountEventsController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAccountRequest request, CancellationToken ct)
    {
        var notification = new AccountCreatedNotification(Guid.NewGuid(), request.OwnerName, request.InitialBalance);
        await mediator.Publish(notification, ct);  // fan-out to all handlers
        return Ok(new { notification.AccountId, notification.OwnerName });
    }
}
```

The controller knows about `IMediator` but has **no knowledge** of `SendWelcomeEmailHandler`
or `AccountCreatedAuditHandler` — fully decoupled.

---

## INotification vs C# event keyword

| | C# `event` (Lesson 08-A) | MediatR `INotification` (Lesson 08-B) |
|---|---|---|
| Coupling | subscriber must reference the publisher to `+=` | zero coupling — handlers registered via DI |
| Discovery | compile-time | DI scan via `AddMediatR` |
| Async | requires `async void` (discouraged) | `async Task` natively supported |
| Testing | must wire event manually | inject mock `IMediator` |

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `POST` | `/accounts-events` | Publishes `AccountCreatedNotification` to all handlers |
| `GET` | `/accounts-events/audit` | Returns audit log populated by `AccountCreatedAuditHandler` |
| `DELETE` | `/accounts-events/audit/reset` | Test helper |

---

## Project Structure (new / changed files)

```
Lesson/
  Notifications/
    AccountCreatedNotification.cs   NEW  INotification record
  Handlers/
    AccountNotificationHandlers.cs  NEW  SendWelcomeEmailHandler + AccountCreatedAuditHandler
  Controllers/
    AccountEventsController.cs      NEW  /accounts-events/* endpoints
Lesson.Tests/
  MediatRNotificationTests.cs       NEW  7 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~MediatRNotificationTests"
# 7 tests — all pass
```

| Test | What it verifies |
|---|---|
| `Create_Returns200AndAccountId` | Publish succeeds; Guid returned |
| `Create_OwnerNamePreservedInResponse` | Data flows through notification |
| `AfterCreate_AuditHandlerReceivesNotification` | Fan-out — audit handler called |
| `AfterCreate_AuditEntryHasCorrectBalance` | Data integrity through the pipeline |
| `MultipleCreates_AllRecordedInAuditLog` | Multiple notifications accumulated |
| `ResetAudit_ClearsLog` | Test helper works |
| `Create_WithZeroBalance_StillPublishesAndRecords` | No validation barrier at this level |

---

## Exercises

1. Add a `TransactionCompletedNotification` with a third handler that sends an SMS (simulated) — verify all three handlers run with a single `Publish` call.
2. Throw an exception inside `SendWelcomeEmailHandler` and observe MediatR's default behaviour (it propagates by default). Then wrap it with a try/catch inside the handler.
3. Add `[Transactional]`-style behaviour: use `IPipelineBehavior` to wrap `Publish` in a using block that logs start/end.
4. Replace `IMediator.Publish` with `IMediator.Send` (returning a value) and compare the semantics — `Send` expects exactly one handler; `Publish` allows zero or many.


> **Branch:** `lesson/08-events/a-basic`
> **Prerequisites:** Lesson 07-C (custom exception hierarchy, MediatR)

---

## What you will learn

| Topic | C# | Java parallel |
|---|---|---|
| `delegate` | type-safe multicast function pointer | `java.util.function.*` / custom `@FunctionalInterface` |
| `event` keyword | restricts delegate — external code can only `+=` / `-=` | `ApplicationEventPublisher` / custom listener pattern |
| `EventHandler<T>` | standard `(object? sender, T args)` signature | `ApplicationListener<E>` |
| `EventArgs` subclass | typed event payload | `ApplicationEvent` subclass |
| `Action<T>` | delegate taking a parameter, returning void | `Consumer<T>` |
| `Func<T, TResult>` | delegate taking a parameter, returning a value | `Function<T, R>` |
| Multicast delegate | `handler += subscriber` — all subscribers called | `List<ApplicationListener>` |
| In-process event bus | singleton class that holds and fires the event | `@Component` + `ApplicationEventPublisher` |

---

## 1. C# Delegates

A delegate is a **type that holds a reference to a method** (or multiple methods — multicast).

```csharp
// Action<T> — void return
Action<string> log = msg => Console.WriteLine(msg);
log("hello");

// Func<T, TResult> — non-void return
Func<decimal, decimal> tax = amount => amount * 1.2m;
var total = tax(100m); // 120

// Multicast — += adds a subscriber
Action<string> multi = s => { };
multi += s => Console.WriteLine($"[A] {s}");
multi += s => Console.WriteLine($"[B] {s}");
multi("fired"); // both A and B are called
```

**Java parallel:** `Consumer<String>` for `Action<T>`, `Function<BigDecimal,BigDecimal>` for `Func<T,R>`.

---

## 2. event keyword

`event` wraps a delegate and enforces encapsulation:
- **Inside** the class: can invoke (raise) the event with `?.Invoke(...)`.
- **Outside** the class: can only subscribe (`+=`) or unsubscribe (`-=`).

```csharp
public class DomainEventBus
{
    public event EventHandler<PaymentCreatedEventArgs>? PaymentCreated;

    public void PublishPaymentCreated(PaymentCreatedEventArgs args) =>
        PaymentCreated?.Invoke(this, args);   // null-safe raise
}
```

**Java parallel:**
```java
@Component
public class DomainEventBus {
    @Autowired ApplicationEventPublisher publisher;

    public void publishPaymentCreated(PaymentCreatedEvent event) {
        publisher.publishEvent(event);
    }
}
```

---

## 3. Subscriber

```csharp
public class PaymentAuditSubscriber
{
    public PaymentAuditSubscriber(DomainEventBus bus)
    {
        bus.PaymentCreated += OnPaymentCreated;   // subscribe
    }

    private void OnPaymentCreated(object? sender, PaymentCreatedEventArgs args) =>
        _log.Add(args);
}
```

**Java parallel:**
```java
@Component
public class PaymentAuditListener {
    @EventListener
    public void onPaymentCreated(PaymentCreatedEvent event) { ... }
}
```

---

## 4. Registration in Program.cs

```csharp
// Both are Singleton — the subscriber is created eagerly to connect the event wire
builder.Services.AddSingleton<DomainEventBus>();
builder.Services.AddSingleton<PaymentAuditSubscriber>();
```

The ASP.NET Core DI container resolves `PaymentAuditSubscriber` on first use (lazy Singleton).
The constructor subscribes to `DomainEventBus.PaymentCreated` at that point.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `POST` | `/event-demo/payment` | Publishes `PaymentCreated` event |
| `GET` | `/event-demo/audit` | Returns in-memory audit log |
| `GET` | `/event-demo/delegate-demo` | Illustrates `Action<T>`, `Func<T,R>`, multicast |

---

## Project Structure (new / changed files)

```
Lesson/
  Events/
    DomainEventBus.cs           NEW  event keyword, PaymentCreatedEventArgs
  Subscribers/
    PaymentAuditSubscriber.cs   NEW  subscribes via +=, maintains audit log
  Controllers/
    EventDemoController.cs      NEW  /event-demo/* endpoints
  Program.cs                          + AddSingleton for bus and subscriber
Lesson.Tests/
  EventBasicTests.cs            NEW  7 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~EventBasicTests"
# 7 tests — all pass
```

| Test | What it verifies |
|---|---|
| `PublishPayment_Returns200` | Event publish succeeds |
| `PublishPayment_ResponseContainsPaymentId` | Guid returned |
| `AfterPublish_AuditLogContainsEntry` | Subscriber received the event |
| `MultiplePublishes_AllRecordedInAuditLog` | Multicast — all events recorded |
| `AuditEntry_ContainsCorrectAmount` | Event data is correct |
| `DelegateDemo_Returns200` | Delegate demo endpoint works |
| `DelegateDemo_FuncResultIs120` | `Func<decimal,decimal>` applies 20% tax correctly |

---

## Exercises

1. Add a second subscriber `PaymentEmailSubscriber` that accumulates `(fromAccount, toAccount)` pairs and expose a `/event-demo/emails` endpoint to verify it.
2. Unsubscribe from the event (`-=`) inside `PaymentAuditSubscriber` after recording 3 events and verify the log stops growing.
3. Change `OnPaymentCreated` to be an async method and observe the compilation error — then read about why `async void` event handlers are generally discouraged.
4. Add `Predicate<T>` (another built-in delegate) to the delegate demo: filter payments above a threshold amount.


> **Branch:** `lesson/07-error-handling/c-advanced`
> **Prerequisites:** Lesson 07-B (IExceptionHandler, ProblemDetails, FluentValidation)

---

## What you will learn

| Topic | C# ASP.NET Core | Java parallel |
|---|---|---|
| Custom exception hierarchy | `DomainException` base ? typed subclasses | `RuntimeException` hierarchy + `@ResponseStatus` |
| `NotFoundException` ? 404 | thrown from handler, caught globally | `ResponseStatusException(NOT_FOUND)` |
| `BusinessRuleException` ? 422 | domain rule violation | `ResponseStatusException(UNPROCESSABLE_ENTITY)` |
| Multiple `IExceptionHandler` | **registration order = evaluation order** | `@ExceptionHandler` specificity in `@ControllerAdvice` |
| `IPipelineBehavior<TReq, TRes>` | MediatR middleware (validation, logging, etc.) | Spring AOP `@Around` advice |
| `ValidationBehavior<T>` | runs FluentValidation before every handler | `@Validated` service + `MethodValidationInterceptor` |

---

## 1. Custom Exception Hierarchy

```csharp
public abstract class DomainException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public class NotFoundException(string resource, object key)
    : DomainException($"{resource} '{key}' was not found.", 404);

public class BusinessRuleException(string message)
    : DomainException(message, 422);

public class ForbiddenException(string message)
    : DomainException(message, 403);
```

Exceptions carry their own HTTP status — the handler needs no `if/else` chain.

**Java parallel:**
```java
@ResponseStatus(HttpStatus.NOT_FOUND)
public class NotFoundException extends RuntimeException { ... }
```

---

## 2. DomainExceptionHandler + Handler Registration Order

```csharp
// Specific handlers FIRST — GlobalExceptionHandler is the catch-all at the end
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
```

`DomainExceptionHandler` returns `false` if the exception is not a `DomainException`,
passing control to the next registered handler.

**Java parallel:** `@ExceptionHandler` methods are matched by most-specific type first;
`@ExceptionHandler(Exception.class)` is the catch-all.

---

## 3. MediatR IPipelineBehavior — Validation Middleware

```csharp
public class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var failures = (await Task.WhenAll(
                validators.Select(v => v.ValidateAsync(request, ct))))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures); // caught by ValidationExceptionHandler

        return await next();
    }
}
```

**Registered once** — applies to ALL MediatR requests:
```csharp
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
```

**Java parallel:** Spring AOP `@Around` `@Validated` service interceptor.

---

## 4. Command + Handler pattern

```csharp
public record CreatePaymentCommand(string FromAccount, string ToAccount, decimal Amount)
    : IRequest<PaymentResult>;

public class CreatePaymentHandler : IRequestHandler<CreatePaymentCommand, PaymentResult>
{
    public Task<PaymentResult> Handle(CreatePaymentCommand request, CancellationToken ct)
    {
        if (_blocked.Contains(request.FromAccount))
            throw new BusinessRuleException($"Account {request.FromAccount} is blocked.");

        return Task.FromResult(new PaymentResult(Guid.NewGuid(), ...));
    }
}
```

**Java parallel:** Spring `@Service` method called by the controller — MediatR decouples the
controller from the handler via message-passing.

---

## Exception Handler Pipeline (all three parts combined)

```
Unhandled exception
  ? DomainExceptionHandler    (returns true for DomainException subtypes)
  ? ValidationExceptionHandler (returns true for FluentValidation.ValidationException)
  ? GlobalExceptionHandler    (catch-all ? 500 ProblemDetails)
```

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `POST` | `/payments` | MediatR pipeline ? validation ? handler |
| `GET` | `/payments/{id}` | Always throws NotFoundException ? 404 |
| `GET` | `/payments/forbidden` | Throws ForbiddenException ? 403 |

---

## Project Structure (new / changed files)

```
Lesson/
  Exceptions/
    DomainException.cs             NEW  DomainException + NotFoundException + BusinessRuleException + ForbiddenException
  ExceptionHandlers/
    DomainExceptionHandler.cs      NEW  Maps DomainException ? ProblemDetails by StatusCode
    ValidationExceptionHandler.cs  NEW  Maps FluentValidation.ValidationException ? 400
  Pipeline/
    ValidationBehavior.cs          NEW  IPipelineBehavior — runs validators before handler
  Commands/
    CreatePaymentCommand.cs        NEW  IRequest + validator + IRequestHandler
  Controllers/
    PaymentsController.cs          NEW  /payments/* endpoints
  Program.cs                              + ordered exception handler registration + MediatR
Lesson.Tests/
  ErrorHandlingAdvancedTests.cs    NEW  9 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~ErrorHandlingAdvancedTests"
# 9 tests — all pass
```

| Test | What it verifies |
|---|---|
| `Get_PaymentNotFound_Returns404` | NotFoundException ? 404 |
| `Get_NotFound_ResponseIsProblemDetails` | ProblemDetails status field |
| `Forbidden_Returns403` | ForbiddenException ? 403 |
| `Forbidden_ResponseIsProblemDetails` | ProblemDetails for 403 |
| `Create_WhenAmountZero_Returns400ViaMediatRPipeline` | ValidationBehavior catches invalid command |
| `Create_WhenSameAccount_Returns400WithValidationError` | Cross-property rule in MediatR pipeline |
| `Create_WhenAccountBlocked_Returns422BusinessRule` | BusinessRuleException from handler ? 422 |
| `Create_WithValidCommand_Returns201` | Happy path |
| `Create_WithValidCommand_ResponseContainsPaymentId` | Response body has Guid |

---

## Exercises

1. Add a `ConflictException` (409) and throw it when the same `FromAccount + ToAccount + Amount` combination is submitted twice.
2. Add a `LoggingBehavior<TRequest, TResponse>` that logs the command type, execution time, and whether it succeeded or threw.
3. Create a second `IRequestHandler` with its own `AbstractValidator` and verify the `ValidationBehavior` runs the correct validator per command type.
4. Move `ValidationExceptionHandler` after `GlobalExceptionHandler` and observe which tests fail — then restore the correct order.


> **Branch:** `lesson/07-error-handling/b-intermediate`
> **Prerequisites:** Lesson 07-A (try/catch, Data Annotations, ModelState)

---

## What you will learn

| Topic | C# ASP.NET Core | Java parallel |
|---|---|---|
| `IExceptionHandler` (.NET 8+) | global unhandled exception handler | `@ControllerAdvice` + `@ExceptionHandler(Exception.class)` |
| `ProblemDetails` (RFC 7807) | standardised JSON error format | Spring 6 `ProblemDetail` |
| `AddExceptionHandler<T>()` | DI registration of the handler | `@Bean ExceptionResolver` |
| `FluentValidation` | rule-based validation in a separate class | Hibernate Validator `ConstraintValidator<A,T>` |
| Cross-property rules | `.NotEqual(x => x.From)` | `@ScriptAssert` / custom `@Constraint` |

---

## 1. IExceptionHandler — Global Error Handling

```csharp
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);

        var problem = new ProblemDetails
        {
            Status = 500,
            Title  = "An unexpected error occurred.",
            Detail = exception.Message,
            Type   = "https://tools.ietf.org/html/rfc9110#section-15.6.1"
        };
        httpContext.Response.StatusCode = 500;
        await httpContext.Response.WriteAsJsonAsync(problem, ct);

        return true; // stop handler chain
    }
}
```

**Registration:**
```csharp
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
// ...
app.UseExceptionHandler();
```

**Java parallel:**
```java
@ControllerAdvice
public class GlobalHandler {
    @ExceptionHandler(Exception.class)
    public ResponseEntity<ProblemDetail> handle(Exception ex) {
        var pd = ProblemDetail.forStatusAndDetail(HttpStatus.INTERNAL_SERVER_ERROR, ex.getMessage());
        return ResponseEntity.of(pd).build();
    }
}
```

---

## 2. ProblemDetails (RFC 7807) shape

```json
{
  "type":    "https://tools.ietf.org/html/rfc9110#section-15.6.1",
  "title":   "An unexpected error occurred.",
  "status":  500,
  "detail":  "This was not caught by the action.",
  "traceId": "00-abc123..."
}
```

`[ApiController]` automatically returns `ValidationProblemDetails` (extends `ProblemDetails`)
for model validation failures — same format, status 400 with an `errors` dictionary.

---

## 3. FluentValidation

```csharp
public class CreateTransferRequestValidator : AbstractValidator<CreateTransferRequest>
{
    public CreateTransferRequestValidator()
    {
        RuleFor(x => x.FromAccount)
            .NotEmpty()
            .Length(5, 20);

        RuleFor(x => x.ToAccount)
            .NotEmpty()
            .Length(5, 20)
            .NotEqual(x => x.FromAccount)   // cross-property rule
            .WithMessage("Source and destination accounts must differ.");

        RuleFor(x => x.Amount)
            .InclusiveBetween(0.01m, 1_000_000m);
    }
}
```

**Registered via assembly scan:**
```csharp
builder.Services.AddValidatorsFromAssemblyContaining<CreateTransferRequestValidator>();
```

**Used manually in a controller** (Lesson 07-C shows pipeline integration with MediatR):
```csharp
var result = await validator.ValidateAsync(request, ct);
if (!result.IsValid)
    return ValidationProblem(new ValidationProblemDetails(...));
```

**Java parallel:** `@Autowired Validator validator; validator.validate(request)` — same manual
approach before integrating with `@Valid`.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/error-demo/unhandled` | Throws unhandled ? `GlobalExceptionHandler` ? 500 ProblemDetails |
| `POST` | `/error-demo/fluent-validate` | Validates via FluentValidation; 400 or 200 |

---

## Project Structure (new / changed files)

```
Lesson/
  ExceptionHandlers/
    GlobalExceptionHandler.cs    NEW  IExceptionHandler ? ProblemDetails 500
  Validators/
    CreateTransferRequestValidator.cs  NEW  FluentValidation rules
  Controllers/
    ErrorDemoController.cs       NEW  /error-demo/* endpoints
  Program.cs                           + AddExceptionHandler, AddProblemDetails,
                                         AddValidatorsFromAssemblyContaining,
                                         app.UseExceptionHandler()
Lesson.Tests/
  ErrorHandlingIntermediateTests.cs  NEW  9 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~ErrorHandlingIntermediateTests"
# 9 tests — all pass
```

| Test | What it verifies |
|---|---|
| `UnhandledException_Returns500` | Global handler catches unhandled exception |
| `UnhandledException_ResponseIsProblemDetails` | Status field = 500 |
| `UnhandledException_ProblemDetails_HasTitle` | Title is present |
| `UnhandledException_ProblemDetails_DetailContainsExceptionMessage` | Detail contains exception message |
| `FluentValidate_WhenAmountTooLow_Returns400` | Range rule violation |
| `FluentValidate_WhenSameAccount_Returns400WithCrossPropertyError` | Cross-property rule |
| `FluentValidate_WhenAccountTooShort_Returns400` | Length rule violation |
| `FluentValidate_WithValidPayload_Returns200` | Happy path |
| `FluentValidate_WithValidPayload_ResponseContainsAmount` | Response body correct |

---

## Exercises

1. Register a second `IExceptionHandler` that specifically handles `ArgumentException` with 422 Unprocessable Entity — handlers are called in registration order.
2. Add a `MustAsync` rule to `CreateTransferRequestValidator` that checks account numbers are not on a "blocked list" (simulate with a list in memory).
3. Replace the manual `validator.ValidateAsync(...)` in `ErrorDemoController` with a global `IActionFilter` that runs FluentValidation automatically for all POST/PUT requests.
4. Add an `extensions` field to the `ProblemDetails` (e.g. `correlationId`) pulled from `HttpContext.Items`.


> **Branch:** `lesson/07-error-handling/a-basic`
> **Prerequisites:** Lesson 06-C (advanced filters)

---

## What you will learn

| Topic | C# ASP.NET Core | Java parallel |
|---|---|---|
| `try/catch` in controllers | catch ? map to `IActionResult` | `@ExceptionHandler` in controller |
| `[ApiController]` auto-validation | returns 400 `ValidationProblemDetails` automatically | `@Valid` + `MethodArgumentNotValidException` |
| Data Annotations | `[Required]`, `[Range]`, `[StringLength]`, `[MaxLength]` | Bean Validation: `@NotNull`, `@Size`, `@Min`, `@Max` |
| `ModelState` | dictionary of field ? error list | `BindingResult` |
| HTTP error responses | `BadRequest()`, `NotFound()`, `StatusCode(500,...)` | `ResponseEntity<>` |

---

## 1. Data Annotations

Attributes placed on model properties declare validation rules.
ASP.NET Core evaluates them automatically during model binding.

```csharp
public class CreateTransferRequest
{
    [Required(ErrorMessage = "Source account number is required.")]
    [StringLength(20, MinimumLength = 5)]
    public string FromAccount { get; set; } = string.Empty;

    [Range(0.01, 1_000_000)]
    public decimal Amount { get; set; }

    [MaxLength(200)]
    public string? Description { get; set; }
}
```

**Java parallel:**
```java
public record CreateTransferRequest(
    @NotBlank @Size(min=5, max=20) String fromAccount,
    @DecimalMin("0.01") @DecimalMax("1000000") BigDecimal amount,
    @Size(max=200) String description
) {}
```

---

## 2. [ApiController] automatic validation

When a controller is decorated with `[ApiController]`, ASP.NET Core checks `ModelState`
before the action method runs. If validation fails it returns a `400 ValidationProblemDetails`
**without any extra code in the action**.

```csharp
[ApiController]
[Route("transfers")]
public class TransferController : ControllerBase
{
    [HttpPost]
    public IActionResult Create([FromBody] CreateTransferRequest request)
    {
        // If annotations were violated, this line is never reached.
        // ...
    }
}
```

**Java parallel:** Spring MVC's `DefaultHandlerExceptionResolver` converts
`MethodArgumentNotValidException` to 400 when `@Valid` is used.

---

## 3. try/catch ? IActionResult

```csharp
try
{
    if (request.FromAccount == request.ToAccount)
        return BadRequest(new { error = "Source and destination accounts must differ." });

    // ... business logic
}
catch (Exception ex)
{
    return StatusCode(500, new { error = "Unexpected error.", detail = ex.Message });
}
```

Lesson 07-B replaces per-action try/catch with a global exception handler.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `POST` | `/transfers` | Validated by annotations; 201 on success, 400 on violation |
| `GET` | `/transfers/{id}` | 404 if not found |
| `DELETE` | `/transfers/reset` | Test helper — clears in-memory list |
| `GET` | `/transfers/simulate-error` | Forces a caught exception ? 500 |

---

## Project Structure (new / changed files)

```
Lesson/
  Models/
    CreateTransferRequest.cs   NEW  Data Annotations validation model
  Controllers/
    TransferController.cs      NEW  try/catch, ModelState, IActionResult errors
Lesson.Tests/
  ErrorHandlingBasicTests.cs   NEW  9 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~ErrorHandlingBasicTests"
# 9 tests — all pass
```

| Test | What it verifies |
|---|---|
| `Create_WhenAmountIsZero_Returns400` | `[Range]` violation ? 400 |
| `Create_WhenFromAccountMissing_Returns400WithErrors` | `[Required]` ? 400 with error dictionary |
| `Create_WhenAccountNumberTooShort_Returns400` | `[StringLength]` min ? 400 |
| `Create_WhenDescriptionTooLong_Returns400` | `[MaxLength]` ? 400 |
| `Create_WhenSameAccount_Returns400WithDomainError` | Domain rule ? 400 |
| `Create_WithValidPayload_Returns201` | Happy path ? 201 |
| `GetById_WhenNotFound_Returns404` | Not found ? 404 |
| `GetById_AfterCreate_Returns200` | Persisted correctly ? 200 |
| `SimulateError_Returns500WithMessage` | try/catch ? 500 with message |

---

## Exercises

1. Add a custom `[FutureDate]` attribute that validates a `DateTime` is in the future.
2. Add a `[FromQuery]` parameter with `[Range]` to one of the GET endpoints and verify validation applies.
3. Disable `[ApiController]`'s automatic validation suppression and add an explicit `if (!ModelState.IsValid) return ValidationProblem(ModelState)` — verify the same tests pass.
4. Add a second domain rule: `Amount` must be divisible by `0.01` (i.e. at most 2 decimal places).


> **Branch:** `lesson/06-middleware/c-advanced`
> **Prerequisites:** Lesson 06-B (IActionFilter, IAsyncActionFilter)

---

## What you will learn

| Topic | C# ASP.NET Core | Java parallel |
|---|---|---|
| `IResourceFilter` | wraps model binding; can cache / short-circuit early | Servlet Filter checking ETags |
| `IResultFilter` | wraps result execution; can transform the response body | `ResponseBodyAdvice<T>.beforeBodyWrite()` |
| `IEndpointFilter` | minimal-API equivalent of `IActionFilter` (.NET 7+) | `HandlerInterceptor` on a specific path |
| `[TypeFilter(typeof(T))]` | apply a filter per-action with DI support | — |
| `context.Result = ...` | short-circuit (resource filter) | `filterChain.doFilter()` not called |

---

## 1. Filter Execution Order

```
Request
  ? [Middleware]
    ? IResourceFilter.OnResourceExecuting   ? can short-circuit (cache hit)
      ? Model binding
        ? IActionFilter.OnActionExecuting
          ? Action method
        ? IActionFilter.OnActionExecuted
      ? IResultFilter.OnResultExecuting      ? can transform the result
        ? Write response body
      ? IResultFilter.OnResultExecuted
    ? IResourceFilter.OnResourceExecuted     ? response written; can log/audit
```

Use `IResourceFilter` when you need to act **before model binding** (e.g. caching).
Use `IResultFilter` when you need to **transform the serialized response**.

---

## 2. ResponseCacheFilter (IResourceFilter)

```csharp
public class ResponseCacheFilter : IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (_cache.TryGetValue(CacheKey(context.HttpContext), out var cached))
            context.Result = cached; // short-circuit — action never runs
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
        if (context.Result is ObjectResult result)
            _cache[CacheKey(context.HttpContext)] = result; // populate cache
    }
}
```

On a cache hit the action is **never invoked** — not even model binding runs.

---

## 3. EnvelopeResultFilter (IResultFilter)

```csharp
public class EnvelopeResultFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { StatusCode: null or >= 200 and < 300 } r)
            context.Result = new ObjectResult(new
            {
                data = r.Value,
                meta = new { timestamp = DateTime.UtcNow, version = "06-C" }
            });
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
```

**Java parallel:** `ResponseBodyAdvice<T>` — modify the return value before Jackson serialises it.

---

## 4. ApiKeyEndpointFilter (IEndpointFilter — minimal API)

```csharp
public class ApiKeyEndpointFilter(string requiredKey) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var provided = context.HttpContext.Request.Query["apiKey"].ToString();
        if (provided != requiredKey)
            return Results.Unauthorized(); // short-circuit

        return await next(context);
    }
}
```

**Registered on a minimal-API route:**
```csharp
app.MapGet("/minimal/secure", () => Results.Ok(new { secret = "you have the key!" }))
   .AddEndpointFilter(new ApiKeyEndpointFilter("lesson06"));
```

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/advanced-filters/cached` | Served from cache on second call |
| `DELETE` | `/advanced-filters/reset-cache` | Clears the in-process cache |
| `GET` | `/advanced-filters/envelope` | Result wrapped in `{ data, meta }` |
| `GET` | `/minimal/secure?apiKey=lesson06` | Protected by `ApiKeyEndpointFilter` |

---

## Project Structure (new / changed files)

```
Lesson/
  Filters/
    ResponseCacheFilter.cs      NEW  IResourceFilter — in-process response cache
    EnvelopeResultFilter.cs     NEW  IResultFilter — { data, meta } envelope
    ApiKeyEndpointFilter.cs     NEW  IEndpointFilter — apiKey query-param guard
  Controllers/
    AdvancedFilterController.cs NEW  /advanced-filters/* endpoints
  Program.cs                          + minimal-API /minimal/secure route
Lesson.Tests/
  AdvancedFilterTests.cs        NEW  9 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~AdvancedFilterTests"
# 9 tests — all pass
```

| Test | What it verifies |
|---|---|
| `Cached_FirstCall_Returns200` | Cache miss — action runs, 200 returned |
| `Cached_SecondCall_ReturnsCachedResult` | Cache hit — `callCount` stays at 1 |
| `Envelope_Returns200` | Filter is transparent to status code |
| `Envelope_ResponseBodyContainsDataAndMeta` | Result wrapped in `{ data, meta }` |
| `Envelope_MetaContainsVersion` | Meta contains `version: "06-C"` |
| `SecureEndpoint_WithoutApiKey_Returns401` | Missing key ? 401 |
| `SecureEndpoint_WithWrongApiKey_Returns401` | Wrong key ? 401 |
| `SecureEndpoint_WithCorrectApiKey_Returns200` | Correct key ? 200 |
| `SecureEndpoint_WithCorrectApiKey_ReturnsSecret` | Body contains secret value |

---

## Exercises

1. Extend `ResponseCacheFilter` to honour a `?bust=1` query parameter that forces a cache bypass and refresh.
2. Add an `X-Cache: HIT` / `X-Cache: MISS` response header inside `ResponseCacheFilter`.
3. Convert `ApiKeyEndpointFilter` to read the key from `IConfiguration` instead of a constructor argument.
4. Apply `EnvelopeResultFilter` **globally** and observe which existing tests would need updating.


> **Branch:** `lesson/06-middleware/b-intermediate`
> **Prerequisites:** Lesson 06-A (custom middleware, IMiddleware)

---

## What you will learn

| Topic | C# ASP.NET Core | Java parallel |
|---|---|---|
| `IActionFilter` | sync filter (OnActionExecuting / OnActionExecuted) | `HandlerInterceptor` (`preHandle` / `postHandle`) |
| `IAsyncActionFilter` | async filter with `ActionExecutionDelegate next` | `HandlerInterceptor` (async variant) |
| Global registration | `AddControllers(o => o.Filters.Add<T>())` | `WebMvcConfigurer.addInterceptors()` |
| Per-action attribute | `[TypeFilter(typeof(T))]` | `@Annotation` on method |
| Correlation ID | read from request header, store in `HttpContext.Items`, echo back | `ThreadLocal` + `preHandle` / `postHandle` |
| Short-circuiting | set `context.Result` without calling `next()` | `preHandle()` returning `false` |

---

## 1. IActionFilter vs IMiddleware

| | `IMiddleware` | `IActionFilter` |
|---|---|---|
| Scope | Every request (incl. static files, unknown routes) | Only requests that reach a controller action |
| Access to action metadata | ? | ? (`ActionDescriptor`, `ActionArguments`) |
| Short-circuit | Set `context.Result` in `OnResultExecuting` | Set `context.Result` in `OnActionExecuting` |
| Async | Implement `InvokeAsync` | Implement `IAsyncActionFilter` |

Use **middleware** for cross-cutting concerns that apply to all requests (logging, headers).  
Use **action filters** when you need access to MVC-specific context (model binding, action parameters).

---

## 2. CorrelationIdFilter (global, IActionFilter)

```csharp
public class CorrelationIdFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var correlationId = context.HttpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                            ?? Guid.NewGuid().ToString();

        context.HttpContext.Items["CorrelationId"] = correlationId;          // store for logging
        context.HttpContext.Response.Headers["X-Correlation-Id"] = correlationId; // echo back
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
```

**Registered globally:**
```csharp
builder.Services.AddControllers(options =>
    options.Filters.Add<CorrelationIdFilter>());
```

**Java parallel:**
```java
public class CorrelationInterceptor implements HandlerInterceptor {
    public boolean preHandle(HttpServletRequest request, HttpServletResponse response, Object handler) {
        String id = Optional.ofNullable(request.getHeader("X-Correlation-Id"))
                            .orElse(UUID.randomUUID().toString());
        response.setHeader("X-Correlation-Id", id);
        return true;
    }
}
```

---

## 3. RequireBodyFilter (per-action, IAsyncActionFilter)

```csharp
public class RequireBodyFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var hasNullBodyParam = context.ActionDescriptor.Parameters
            .Any(p => p.BindingInfo?.BindingSource?.Id == "Body"
                      && context.ActionArguments.TryGetValue(p.Name, out var val)
                      && val is null);

        if (hasNullBodyParam)
        {
            context.Result = new BadRequestObjectResult(
                new { error = "Request body is required." });
            return; // short-circuit — action never runs
        }

        await next();
    }
}
```

**Applied per-action:**
```csharp
[HttpPost("body-required")]
[TypeFilter(typeof(RequireBodyFilter))]
public IActionResult BodyRequired([FromBody] SamplePayload? payload) => Ok(...);
```

**Java parallel:** `preHandle()` returning `false` — the controller method is never invoked.

---

## 4. Pipeline execution order

```
Request
  ? Middleware (ResponseHeaderMiddleware)
  ? Middleware (RequestLoggingMiddleware)
  ? MVC Router
    ? Filter: OnActionExecuting (CorrelationIdFilter)
    ? Filter: OnActionExecutionAsync (RequireBodyFilter) [if applied]
      ? Action Method
    ? Filter: OnActionExecuted (CorrelationIdFilter)
Response
```

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/filters/echo` | Returns correlation ID from `HttpContext.Items` |
| `GET` | `/filters/echo-header` | Echoes `X-Correlation-Id` from request header |
| `POST` | `/filters/body-required` | 400 if body missing (RequireBodyFilter), 200 otherwise |

---

## Project Structure (new / changed files)

```
Lesson/
  Filters/
    CorrelationIdFilter.cs   NEW  IActionFilter — global correlation ID
    RequireBodyFilter.cs     NEW  IAsyncActionFilter — short-circuit on null body
  Controllers/
    FilterDemoController.cs  NEW  /filters/* endpoints
  Program.cs                      + global CorrelationIdFilter registration
Lesson.Tests/
  ActionFilterTests.cs       NEW  8 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~ActionFilterTests"
# 8 tests — all pass
```

| Test | What it verifies |
|---|---|
| `Echo_ResponseAlwaysContainsCorrelationIdHeader` | Header present on every response |
| `Echo_WhenNoCorrelationIdSent_ResponseContainsAutoGeneratedGuid` | Auto-generated when absent |
| `Echo_WhenCorrelationIdSent_SameIdIsEchoedBack` | Client ID is preserved |
| `AnyControllerEndpoint_AlsoReceivesCorrelationIdHeader` | Global filter applies everywhere |
| `Echo_ResponseBody_ContainsCorrelationId` | Value stored in `HttpContext.Items` |
| `BodyRequired_WhenBodyMissing_Returns400` | Short-circuit works |
| `BodyRequired_WhenBodyProvided_Returns200` | Happy path unaffected |
| `BodyRequired_WhenBodyProvided_ResponseContainsValue` | Action runs and returns body |

---

## Exercises

1. Convert `CorrelationIdFilter` to `IAsyncActionFilter` and verify the same tests pass.
2. Add an `ExecutionTimeFilter` that measures the time between `OnActionExecuting` and `OnActionExecuted` and writes it to an `X-Execution-Time-Ms` response header.
3. Create a `[RequireApiKey]` filter attribute that reads `X-Api-Key` from the request header and short-circuits with `401 Unauthorized` if it is absent or wrong.
4. Register `RequireBodyFilter` **globally** and observe which existing tests break — then restore per-action registration.


> **Branch:** `lesson/06-middleware/a-basic`
> **Prerequisites:** Lesson 05-C (Advanced LINQ)

---

## What you will learn

| Topic | C# ASP.NET Core | Java parallel |
|---|---|---|
| `IMiddleware` | preferred DI-managed middleware contract | `OncePerRequestFilter` |
| `RequestDelegate next` | call the next component in the pipeline | `filterChain.doFilter()` |
| Middleware ordering | registration order in `Program.cs` controls execution | `FilterRegistrationBean.setOrder()` |
| Request/response logging | log before/after `next(context)` | `CommonsRequestLoggingFilter` |
| Response header injection | add header before `next()` | `OncePerRequestFilter` — `response.setHeader()` |

---

## 1. The Middleware Pipeline

ASP.NET Core processes every HTTP request through a **pipeline** of middleware components.
Each component can:
- Run code **before** the next component (inbound)
- Call `await next(context)` to pass control forward
- Run code **after** the next component returns (outbound)

```
Request ?  [ResponseHeaderMiddleware] ? [RequestLoggingMiddleware] ? [Router] ? Controller
Response ?                           ?                            ?          ?
```

Registration order in `Program.cs` determines pipeline order.  
Middleware registered **first** wraps everything registered after it.

---

## 2. IMiddleware vs Convention-Based Middleware

| Approach | Lifetime | DI injection |
|---|---|---|
| `IMiddleware` | managed by DI container | full constructor injection ? |
| Convention-based (`Invoke(HttpContext)`) | instantiated once at startup | only singleton-safe services in constructor |

`IMiddleware` is the modern, recommended approach because it integrates cleanly with the DI container.

---

## 3. RequestLoggingMiddleware

```csharp
public class RequestLoggingMiddleware(ILogger<RequestLoggingMiddleware> logger) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("? {Method} {Path}", context.Request.Method, context.Request.Path);

        await next(context);   // pass to next middleware / endpoint

        sw.Stop();
        logger.LogInformation("? {Method} {Path} {StatusCode} ({Elapsed}ms)",
            context.Request.Method, context.Request.Path,
            context.Response.StatusCode, sw.ElapsedMilliseconds);
    }
}
```

**Java parallel:** `OncePerRequestFilter.doFilterInternal()` — call `filterChain.doFilter()`,
then log after it returns.

---

## 4. ResponseHeaderMiddleware

```csharp
public class ResponseHeaderMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        context.Response.Headers["X-Powered-By"] = "ASP.NET Core 10 Lesson 06";
        await next(context);
        // response body has already started streaming — do not write body here
    }
}
```

Headers **must** be set before `next()` is called (or before the response body starts writing).

---

## 5. Registration in Program.cs

```csharp
// Register as transient so DI manages the lifetime
builder.Services.AddTransient<RequestLoggingMiddleware>();
builder.Services.AddTransient<ResponseHeaderMiddleware>();

// Add to pipeline — ORDER MATTERS
app.UseMiddleware<ResponseHeaderMiddleware>();   // outermost wrapper
app.UseMiddleware<RequestLoggingMiddleware>();   // logs every request that reaches it
```

**Java parallel:**
```java
@Bean
public FilterRegistrationBean<RequestLoggingFilter> loggingFilter() {
    var reg = new FilterRegistrationBean<>(new RequestLoggingFilter());
    reg.setOrder(1);
    return reg;
}
```

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/middleware/ping` | Returns `{ message: "pong" }` — used to verify header injection |
| `GET` | `/middleware/slow` | 10 ms delay — verifies elapsed-time logging |

---

## Project Structure (new / changed files)

```
Lesson/
  Middleware/
    RequestLoggingMiddleware.cs  NEW  IMiddleware — logs method, path, status, elapsed
    ResponseHeaderMiddleware.cs  NEW  IMiddleware — injects X-Powered-By header
  Controllers/
    MiddlewareDemoController.cs  NEW  /middleware/ping + /middleware/slow
  Program.cs                          + middleware DI registrations + UseMiddleware calls
Lesson.Tests/
  MiddlewareBasicTests.cs        NEW  7 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~MiddlewareBasicTests"
# 7 tests — all pass
```

| Test | What it verifies |
|---|---|
| `Ping_ResponseContainsXPoweredByHeader` | Header is present |
| `Ping_XPoweredByHeader_ContainsExpectedValue` | Header value contains "ASP.NET Core" |
| `AnyEndpoint_ResponseContainsXPoweredByHeader` | Header applies to all routes |
| `Ping_Returns200_MiddlewareDoesNotBreakPipeline` | Middleware is transparent |
| `Ping_ResponseBody_IsCorrect` | Body is unchanged by middleware |
| `Slow_Returns200_AfterDelay` | Middleware works with delayed responses |
| `UnknownRoute_Returns404_MiddlewareStillAddsHeader` | Header added even on 404 responses |

---

## Exercises

1. Add a `CorrelationIdMiddleware` that reads `X-Correlation-Id` from the request (or generates a new `Guid` if absent) and echoes it back in the response headers.
2. Change `ResponseHeaderMiddleware` to add a `Cache-Control: no-store` header and verify with a test.
3. Add a middleware that short-circuits the pipeline for requests to `/health` and returns `200 OK` directly — bypassing the router and all downstream middleware.
4. Register `RequestLoggingMiddleware` **before** `ResponseHeaderMiddleware` and observe how the log output changes (the status code logged will still be correct because both happen after `next()`).


> **Branch:** `lesson/05-linq/c-advanced`
> **Prerequisites:** Lesson 05-B (IEnumerable vs IQueryable, GroupBy, Join, SelectMany, let)

---

## What you will learn

| Topic | C# | Java parallel |
|---|---|---|
| Custom LINQ extensions | `this IEnumerable<T>` extension methods | static utility methods (no dot-notation) |
| `Aggregate` | general-purpose fold / reduce | `stream().reduce(identity, accumulator)` |
| `Zip` | pair two sequences by index | `IntStream.range + get(i)` |
| `Chunk` | split into fixed-size pages | Guava `Lists.partition` |
| `AsParallel` (PLINQ) | CPU-bound parallelism over thread pool | `stream().parallel()` |
| Expression trees | `Expression<Func<T,bool>>` — build predicates at runtime | Reflection + `Predicate<T>` |
| `IAsyncEnumerable<T>` | async streaming with `await foreach` | Project Reactor `Flux<T>` |

---

## 1. Custom LINQ Extension Methods

Extending `IEnumerable<T>` with a static class makes reusable pipeline steps feel native:

```csharp
public static class ProductExtensions
{
    public static IEnumerable<Product> InStock(
        this IEnumerable<Product> source, int minStock = 1)
        => source.Where(p => p.Stock >= minStock);

    public static IEnumerable<Product> PriceAbove(
        this IEnumerable<Product> source, decimal min)
        => source.Where(p => p.Price >= min);
}

// Usage — reads like built-in LINQ
var result = products.InStock().PriceAbove(50m).MostExpensive(3).ToList();
```

**Java parallel:** static helper methods work but break the fluent chain:
`ProductUtils.mostExpensive(ProductUtils.priceAbove(products, 50), 3)`

---

## 2. Aggregate — General-Purpose Fold

`Aggregate` is the universal accumulator operator (like `reduce` in functional programming):

```csharp
// Sum all inventory values
decimal total = products.Aggregate(0m, (acc, p) => acc + p.Price * p.Stock);

// Build a comma-separated string
string catalogue = products
    .OrderBy(p => p.Name)
    .Aggregate(string.Empty, (acc, p) => acc.Length == 0 ? p.Name : acc + ", " + p.Name);
```

For common aggregates (`Sum`, `Average`, `Max`, `Min`, `Count`) prefer the specialised
operators — they are more readable and EF Core can translate them to SQL.

**Java:** `stream().reduce(BigDecimal.ZERO, (acc, p) -> acc.add(p.getPrice()), BigDecimal::add)`

---

## 3. Zip — Pair Two Sequences by Index

```csharp
var sorted = products.OrderByDescending(p => p.Price);
var ranks  = Enumerable.Range(1, products.Count);

var ranked = sorted
    .Zip(ranks, (p, rank) => new RankedProduct(rank, p.Name, p.Price))
    .ToList();
// ? [ { Rank=1, Name="Laptop Pro", Price=1299 }, … ]
```

`Zip` stops at the shorter sequence. Three-sequence overloads exist:
`a.Zip(b, c)` returns value tuples `(a[i], b[i], c[i])`.

**Java:** `IntStream.range(0, Math.min(a.size(), b.size())).mapToObj(i -> new Pair(a.get(i), b.get(i)))`

---

## 4. Chunk — Split into Fixed-Size Pages

Introduced in .NET 6:

```csharp
// Splits 10 products into pages of 3: [[p1,p2,p3],[p4,p5,p6],[p7,p8,p9],[p10]]
Product[][] pages = products.Chunk(3).ToArray();
```

`Chunk` is ideal for batch-processing large sequences without loading everything at once.

**Java:** `Guava: Lists.partition(list, 3)` or a custom `IntStream` splitter.

---

## 5. AsParallel — PLINQ Basics

```csharp
var expensive = products
    .AsParallel()                            // distribute work across ThreadPool
    .Where(p => p.Price > minPrice)          // runs on multiple threads
    .OrderBy(p => p.Name)                    // re-serialise before output
    .ToList();
```

Guidelines:
- Use for **CPU-bound** work on large collections (> ~1 000 items as a rough threshold).
- For **I/O-bound** work, use `async/await` — PLINQ blocks threads.
- Results are non-deterministic unless you add `AsOrdered()` or a final `OrderBy`.

**Java:** `stream().parallel()` — same concept and same caveats.

---

## 6. Expression Trees — Intro

An `Expression<Func<T, bool>>` stores a LINQ query as a **data structure** (AST) rather
than a compiled delegate. EF Core reads this tree to generate SQL.

```csharp
// Build: p => p.Price < maxPrice
var param    = Expression.Parameter(typeof(Product), "p");
var property = Expression.Property(param, nameof(Product.Price));
var constant = Expression.Constant(maxPrice, typeof(decimal));
var body     = Expression.LessThan(property, constant);
var lambda   = Expression.Lambda<Func<Product, bool>>(body, param);

// Compile and use as a normal delegate
var predicate = lambda.Compile();
var result = products.Where(predicate).ToList();
```

This pattern powers dynamic query builders, AutoMapper projections, and EF Core itself.

**Java parallel:** no direct equivalent; closest is reflection-based predicate construction.

---

## 7. IAsyncEnumerable\<T\> — Async Streaming

`IAsyncEnumerable<T>` lets you produce and consume items **one at a time** asynchronously,
without buffering the entire result:

```csharp
// Producer — async iterator method
public async IAsyncEnumerable<Product> StreamProductsAsync(decimal maxPrice,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    foreach (var p in products.Where(p => p.Price <= maxPrice))
    {
        await Task.Delay(0, ct); // simulate async source (DB cursor, HTTP stream, …)
        yield return p;
    }
}

// Consumer
await foreach (var p in service.StreamProductsAsync(100m))
    Console.WriteLine(p.Name);
```

Use cases: database cursors with EF Core (`IAsyncEnumerable<T>` from `ToAsyncEnumerable`),
file streaming, server-sent events, gRPC streaming.

**Java parallel:** Project Reactor `Flux<T>` or Java 9 `Flow.Publisher<T>`.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/linq/advanced/top-in-stock?minPrice=50&topN=3` | Custom extensions chained |
| `GET` | `/linq/advanced/inventory-value` | `Aggregate` — sum of Price × Stock |
| `GET` | `/linq/advanced/catalogue` | `Aggregate` — comma-separated names |
| `GET` | `/linq/advanced/ranked` | `Zip` — products with price rank |
| `GET` | `/linq/advanced/chunks?pageSize=3` | `Chunk` — pages of products |
| `GET` | `/linq/advanced/parallel?minPrice=50` | `AsParallel` filter |
| `GET` | `/linq/advanced/expression-tree?maxPrice=100` | Runtime-built predicate |
| `GET` | `/linq/advanced/stream?maxPrice=100` | `IAsyncEnumerable<T>` streaming |

---

## Project Structure (new / changed files)

```
Lesson/
  Extensions/
    ProductExtensions.cs       NEW  InStock, Cheapest, MostExpensive, PriceAbove
  Models/
    RankedProduct.cs           NEW  result record for Zip demo
  Services/
    LinqAdvancedService.cs     NEW  Aggregate, Zip, Chunk, PLINQ, ExprTree, IAsyncEnumerable
  Controllers/
    LinqAdvancedController.cs  NEW  /linq/advanced/* endpoints
  Program.cs                        + LinqAdvancedService registered as singleton
Lesson.Tests/
  LinqAdvancedTests.cs         NEW  13 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~LinqAdvancedTests"
# 13 tests — all pass
```

| Test | What it verifies |
|---|---|
| `GetTopInStock_ReturnsAtMostTopN` | Custom extension `MostExpensive` honours `topN` |
| `GetTopInStock_AllProductsExceedMinPrice` | Custom extension `PriceAbove` filters correctly |
| `GetInventoryValue_MatchesManualCalculation` | `Aggregate` sum equals manual calculation |
| `GetCatalogue_ContainsAllProductNames` | `Aggregate` string fold contains every name |
| `GetRanked_CountMatchesProductCount` | `Zip` produces one entry per product |
| `GetRanked_Rank1HasHighestPrice` | Rank 1 corresponds to the most expensive product |
| `GetChunks_PageSize3_ProducesCorrectNumberOfChunks` | `Chunk` creates ceiling(10/3) = 4 pages |
| `GetChunks_TotalItemsEqualsProductCount` | All items preserved across chunks |
| `GetParallel_SameIdsAsSequentialFilter` | `AsParallel` returns same set as sequential |
| `GetByExpressionTree_AllProductsBelowMaxPrice` | Expression-tree predicate filters correctly |
| `GetByExpressionTree_SameResultAsDirectFilter` | Tree result matches hard-coded LINQ filter |
| `StreamProducts_AllNamesAreBelowMaxPrice` | `IAsyncEnumerable` respects max-price filter |
| `StreamProducts_CountMatchesExpected` | Streamed item count matches direct count |

---

## Exercises

1. Add a `SumBy<T>` generic extension method on `IEnumerable<T>` that takes a `Func<T, decimal>` selector — a miniature reimplementation of `Sum`.
2. Use `Aggregate` with a seed of `new Dictionary<string, decimal>()` to build a category ? total-price map in a single pass.
3. Add `AsOrdered()` to the PLINQ pipeline and verify the test still passes — then remove it and observe whether order is preserved across runs.
4. Modify `StreamProductsAsync` to introduce a real `await Task.Delay(1)` and test the endpoint with a short cancellation token to observe `OperationCanceledException` propagation.
5. Build a more complex expression tree: `p => p.Price < maxPrice && p.Category == category` using `Expression.AndAlso`.


> **Branch:** `lesson/05-linq/b-intermediate`
> **Prerequisites:** Lesson 05-A (Where, Select, OrderBy, FirstOrDefault, deferred execution)

---

## What you will learn

| Topic | C# LINQ | Java parallel |
|---|---|---|
| `IEnumerable<T>` vs `IQueryable<T>` | in-memory iteration vs lazy expression tree | `List` stream vs `JpaSpecificationExecutor` |
| `GroupBy` | aggregate by key | `Collectors.groupingBy` |
| `Join` | inner equi-join | `stream().flatMap` + Map lookup |
| `SelectMany` | flatten nested sequences | `stream().flatMap` |
| `let` (query syntax) | introduce an intermediate value | multi-step `.map()` chain |
| Anonymous types | `new { p.Name, p.Price }` | no direct equivalent (use records/tuples) |
| Pipeline chaining | compose operators before materialising | chained stream intermediate ops |

---

## 1. IEnumerable\<T\> vs IQueryable\<T\>

`IEnumerable<T>` iterates in memory; `IQueryable<T>` builds an expression tree that a
provider (EF Core, LINQ to SQL, …) translates to SQL before touching the database.

```csharp
// IEnumerable path — ALL rows loaded first, then filtered in C#
IEnumerable<Product> all = Products.ToList();           // materialise
var result = all.Where(p => p.Category == cat).ToList();

// IQueryable-equivalent — filter is composed before materialisation
IEnumerable<Product> lazy = Products;                   // no iteration yet
lazy = lazy.Where(p => p.Category == cat);              // deferred
var result = lazy.ToList();                             // single pass, only matching elements
```

With a real `DbSet<T>` the lazy path generates `WHERE Category = @cat` in SQL;
the in-memory path loads every row and discards non-matching ones in C#.

**Rule:** keep filters on `IQueryable` until you need the data.

---

## 2. GroupBy

```csharp
var stats = products
    .GroupBy(p => p.Category)
    .Select(g => new CategorySummary(
        g.Key,
        g.Count(),
        g.Sum(p => p.Price),
        g.Average(p => (double)p.Price)))
    .OrderBy(s => s.Category)
    .ToList();
```

**Java:** `stream().collect(Collectors.groupingBy(Product::getCategory, Collectors.counting()))`

---

## 3. Join — Equi-Join Two Sequences

```csharp
var lines = orders.Join(
    products,
    o => o.ProductId,           // outer key
    p => p.Id,                  // inner key
    (o, p) => new OrderLine(
        o.Id, o.CustomerId, p.Name, p.Category,
        p.Price, o.Quantity, p.Price * o.Quantity))
    .ToList();
```

**Java:** `orders.stream().flatMap(o -> products.stream().filter(p -> p.getId() == o.getProductId()).map(p -> new OrderLine(...)))`
(prefer a `Map<Id, Product>` lookup for O(1) performance)

---

## 4. SelectMany — Flattening

```csharp
// Each group contributes multiple strings; SelectMany flattens them into one sequence
var labels = products
    .GroupBy(p => p.Category)
    .OrderBy(g => g.Key)
    .SelectMany(g => g.Select(p => $"[{g.Key}] {p.Name}"))
    .ToList();
```

**Java:** `categories.stream().flatMap(g -> g.getProducts().stream().map(p -> "[" + g.getKey() + "] " + p.getName()))`

---

## 5. let Clause (Query Syntax)

`let` introduces a named intermediate value inside a query-syntax expression,
avoiding recomputing the same expression in `where` and `select`:

```csharp
var discounted =
    (from p in products
     let d = p.Price * (1 - discountRate)   // compute once
     where d < maxDiscountedPrice            // reuse in filter
     orderby d
     select new DiscountedProduct(p.Name, p.Price, d))  // reuse in projection
    .ToList();
```

**Java:** no `let` keyword; use a `.map()` step that projects to a temporary holder record.

---

## 6. Anonymous Types

```csharp
var projection = products.Select(p => new { p.Name, p.Price }).ToList();
// Type is compiler-generated; only usable within the same method.
```

For cross-method use, prefer named records or tuples.
In EF Core queries, anonymous types in `Select` translate to a `SELECT Name, Price` SQL projection.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/linq/filter-in-memory?category=` | IEnumerable path (materialise then filter) |
| `GET` | `/linq/filter-lazy?category=` | IQueryable-equivalent (filter then materialise) |
| `GET` | `/linq/categories/summary` | `GroupBy` ? category stats |
| `GET` | `/linq/orders/lines` | `Join` products and orders |
| `GET` | `/linq/products/labels` | `SelectMany` flattened labels |
| `GET` | `/linq/products/discounted?discountRate=0.10&maxDiscountedPrice=100` | `let` clause demo |
| `GET` | `/linq/orders/top?topN=3` | Chained pipeline — top N by line total |

---

## Project Structure (new / changed files)

```
Lesson/
  Models/
    Order.cs                 NEW  seed order records for Join demo
    LinqIntermediateDtos.cs  NEW  CategorySummary, OrderLine, DiscountedProduct
  Services/
    LinqIntermediateService.cs  NEW  GroupBy, Join, SelectMany, let, chaining
  Controllers/
    LinqIntermediateController.cs  NEW  /linq/filter-*, /linq/categories/summary,
                                        /linq/orders/lines, /linq/products/labels,
                                        /linq/products/discounted, /linq/orders/top
  Program.cs                      + LinqIntermediateService registered as singleton
Lesson.Tests/
  LinqIntermediateTests.cs  NEW  13 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~LinqIntermediateTests"
# 13 tests — all pass
```

| Test | What it verifies |
|---|---|
| `FilterInMemory_SameResultAsFilterLazy_ForSameCategory` | Both paths return identical product IDs |
| `FilterLazy_ReturnsOnlyMatchingCategory` | Lazy filter returns only the requested category |
| `GetCategorySummaries_ReturnsAllCategories` | GroupBy covers every distinct category |
| `GetCategorySummaries_CountsAreCorrect` | Per-category counts match seed data |
| `GetCategorySummaries_TotalValueMatchesSeedData` | Sum of all totals equals sum of all prices |
| `GetOrderLines_CountMatchesSeedOrders` | Join produces one row per order |
| `GetOrderLines_LineTotalIsUnitPriceTimesQuantity` | LineTotal = UnitPrice × Quantity |
| `GetProductLabels_CountMatchesProductCount` | SelectMany flattens to exactly 10 labels |
| `GetProductLabels_EachLabelContainsCategoryAndName` | Format `[Category] Name` respected |
| `GetDiscounted_AllDiscountedPricesAreBelowMaxPrice` | let filter applied correctly |
| `GetDiscounted_DiscountedPriceEqualsOriginalTimesRate` | Computed discount is accurate |
| `GetTopOrders_Top2_ReturnsExactly2` | Take(2) respected |
| `GetTopOrders_AreOrderedByLineTotalDescending` | Chaining keeps correct sort order |

---

## Exercises

1. Add `GET /linq/categories/summary?minCount=2` — filter `CategorySummary` rows where `Count >= minCount` using a chained `.Where()` after `GroupBy`.
2. Add `GET /linq/orders/by-customer/{customerId}` — use `Join` + `Where` to return only order lines for a specific customer.
3. Rewrite `GetOrderLines` using query syntax with an explicit `join … in … on … equals …` clause and compare readability.
4. Replace `SelectMany` in `GetAllProductLabels` with a nested `foreach` loop and verify the output is identical — then appreciate the brevity of `SelectMany`.


> **Branch:** `lesson/05-linq/a-basic`
> **Prerequisites:** Lesson 04-C (Raw SQL, Compiled Queries, Split Queries)

---

## What you will learn

| Topic | C# LINQ | Java parallel |
|---|---|---|
| Method syntax | `.Where().Select().ToList()` | `stream().filter().map().collect()` |
| Query syntax | `from p in … where … select p` | no direct equivalent (method chains only) |
| `Where` | filter elements | `stream().filter(…)` |
| `Select` | project / transform elements | `stream().map(…)` |
| `OrderBy` / `OrderByDescending` | sort; `ThenBy` for secondary key | `stream().sorted(Comparator…)` |
| `FirstOrDefault` | first match or `null` — never throws | `stream().findFirst().orElse(null)` |
| `ToList` | terminal — materialises the pipeline | `stream().collect(toList())` |
| Deferred execution | pipeline is lazy — work happens at the terminal operator | Java Streams are also lazy |

---

## 1. Method Syntax vs Query Syntax

LINQ provides two syntaxes that compile to identical IL.

```csharp
// Method syntax
var result = products
    .Where(p => p.Category == "Electronics")
    .OrderBy(p => p.Price)
    .Select(p => p.Name)
    .ToList();

// Query syntax (SQL-like)
var result = (from p in products
              where p.Category == "Electronics"
              orderby p.Price
              select p.Name).ToList();
```

Choose whichever reads more clearly for the task. Method syntax is more common for simple
pipelines; query syntax shines when using `let`, `join`, or `group … by`.

**Java parallel:** Java Streams only have method chains — there is no query-syntax equivalent.

---

## 2. Where — Filtering

```csharp
var electronics = products.Where(p => p.Category == "Electronics").ToList();
```

`Where` accepts a predicate and returns every element for which it is `true`.
The predicate is not evaluated until a terminal operator materialises the pipeline.

**Java:** `stream().filter(p -> p.getCategory().equals("Electronics")).collect(toList())`

---

## 3. Select — Projection

```csharp
// Project to a value tuple
var nameAndPrice = products.Select(p => (p.Name, p.Price)).ToList();
```

Only the data you need is materialised; when used with `IQueryable` (EF Core) only those
columns are included in the SQL `SELECT`.

**Java:** `stream().map(p -> new NamePrice(p.getName(), p.getPrice())).collect(toList())`

---

## 4. OrderBy / OrderByDescending

```csharp
var sorted = products
    .OrderByDescending(p => p.Price)  // primary sort key
    .ThenBy(p => p.Name)              // secondary sort key (stable)
    .ToList();
```

`ThenBy` / `ThenByDescending` add secondary keys.
Do **not** chain multiple `OrderBy` calls — each resets the sort order.

**Java:** `stream().sorted(Comparator.comparing(Product::getPrice).reversed().thenComparing(Product::getName))`

---

## 5. FirstOrDefault — Safe Single-Element Lookup

```csharp
Product? found = products.FirstOrDefault(p => p.Id == id);
// Returns null if no match — never throws.
```

| Method | No match | Multiple matches |
|---|---|---|
| `FirstOrDefault` | `null` / `default` | returns first |
| `First` | throws `InvalidOperationException` | returns first |
| `SingleOrDefault` | `null` / `default` | throws |
| `Single` | throws | throws |

**Java:** `stream().filter(p -> p.getId() == id).findFirst().orElse(null)`

---

## 6. Deferred Execution

Building a LINQ pipeline does **not** iterate the source — that work is deferred until a
terminal operator is called.

```csharp
// Steps 1-3: build the pipeline (no iteration yet)
IEnumerable<string> query = products
    .Where(p => p.Price <= 50)   // deferred
    .OrderBy(p => p.Price)       // deferred
    .Select(p => p.Name);        // deferred

// Step 4: terminal operator — iterates ONCE and produces List<string>
List<string> result = query.ToList();
```

Consequence: modifying the source between building and materialising the query is reflected
in the result. Materialise early with `ToList()` / `ToArray()` when you want a snapshot.

**Java parallel:** Java Streams are also lazy. Unlike C# queries, a Java Stream **cannot be reused**
after a terminal operation has been called.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/linq/products` | All products; optional `?category=` filter |
| `GET` | `/linq/products/query-syntax?category=` | Same filter via query syntax |
| `GET` | `/linq/products/name-price` | `Select` projection — name + price only |
| `GET` | `/linq/products/by-price-desc` | `OrderByDescending` + `ThenBy` |
| `GET` | `/linq/products/{id}` | `FirstOrDefault` — 404 if not found |
| `GET` | `/linq/products/affordable?maxPrice=50` | Deferred pipeline materialised at `ToList` |

---

## Project Structure (new / changed files)

```
Lesson/
  Models/
    Product.cs               NEW  simple in-memory record (no EF / database)
  Services/
    LinqService.cs           NEW  static seed data + LINQ demo methods
  Controllers/
    LinqDtos.cs              NEW  ProductResponse, NamePriceDto
    LinqController.cs        NEW  /linq/* endpoints
  Program.cs                      + LinqService registered as singleton
Lesson.Tests/
  LinqBasicTests.cs          NEW  10 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~LinqBasicTests"
# 10 tests — all pass
```

| Test | What it verifies |
|---|---|
| `FilterByCategory_Electronics_ReturnsOnlyElectronics` | `Where` filters correctly |
| `FilterByCategory_UnknownCategory_ReturnsEmpty` | `Where` with no match returns empty |
| `FilterByCategory_NoCategory_ReturnsAll` | No filter returns all 10 products |
| `QuerySyntax_SameResultAsMethodSyntax` | Both syntaxes produce identical results |
| `GetNameAndPrice_ReturnsOnlyNameAndPrice` | `Select` projection returns correct shape |
| `GetByPriceDescending_FirstItemIsHighestPrice` | First item has the maximum price |
| `GetByPriceDescending_IsSorted` | Entire list is in descending price order |
| `FindById_ExistingId_ReturnsProduct` | `FirstOrDefault` finds the right product |
| `FindById_MissingId_ReturnsNotFound` | `FirstOrDefault` returns `null` ? 404 |
| `GetAffordableNames_MaxPrice50_OnlyCheapProducts` | Full deferred pipeline matches expected names |

---

## Exercises

1. Add `GET /linq/products/top/{n}` that uses `Take(n)` to return the `n` most expensive products.
2. Replace the `ToList()` terminal in `GetAffordableProductNames` with `ToArray()` and observe that the tests still pass.
3. Add a `Skip` + `Take` overload to `/linq/products?page=1&pageSize=3` to practice manual pagination over an in-memory collection.
4. Try calling `.Where(…)` twice on the same pipeline and verify that both predicates are applied (they are ANDed together in the iteration).

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
