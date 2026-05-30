// =============================================================================
// LESSON 03-B: Repository Pattern, IQueryable vs IEnumerable, Owned Entities
// (builds on 03-A EF Core baseline)
//
// New registrations:
//   IAccountRepository ? AccountRepository (Scoped)
// =============================================================================
// LESSON 03-A: EF Core CRUD — DbContext, SQLite, Migrations
//
// Entity Framework Core is .NET's official ORM.
//
// Key concepts:
//   DbContext     — Unit of Work + identity map; tracks entity changes in memory.
//                   Java parallel: EntityManager / @PersistenceContext
//   DbSet<T>      — Repository for a specific entity type.
//                   Java parallel: JpaRepository<T, ID>
//   Migrations    — Code-first schema versioning.
//                   Java parallel: Flyway / Liquibase
//   SaveChangesAsync() — Flushes all tracked changes to the DB in a transaction.
//                        Java parallel: entityManager.flush() / @Transactional
//
// Lifetime:
//   DbContext is registered as Scoped (one per HTTP request) — the EF Core default.
//   Never inject a Scoped DbContext into a Singleton service; use IDbContextFactory<T> instead.
// =============================================================================

using Lesson.Configuration;
using Lesson.Data;
using Lesson.Options;
using Lesson.Repositories;
using Lesson.UnitOfWork;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ----- Options carried from Lesson 02 -----
builder.Services
    .AddOptions<BankOptions>()
    .BindConfiguration(BankOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<FeatureFlagOptions>()
    .BindConfiguration(FeatureFlagOptions.SectionName);

builder.Services
    .AddOptions<ConnectionStringOptions>()
    .BindConfiguration(ConnectionStringOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<TransferLimitOptions>("domestic")
    .BindConfiguration("TransferLimits:Domestic")
    .ValidateDataAnnotations();

builder.Services
    .AddOptions<TransferLimitOptions>("international")
    .BindConfiguration("TransferLimits:International")
    .ValidateDataAnnotations();

((IConfigurationBuilder)builder.Configuration).Add(new InMemoryDbConfigurationSource
{
    Data = new Dictionary<string, string?>
    {
        ["CustomConfig:WelcomeMessage"] = "Hello from the custom provider!",
        ["CustomConfig:MaxRetries"] = "3",
        ["CustomConfig:ServiceUrl"] = "https://api.acmebank.internal"
    }
});

// ----- 03-A: EF Core — SQLite DbContext registration -----
// AddDbContext registers BankingDbContext as Scoped.
// The connection string is read from appsettings.json "ConnectionStrings:BankDb".
// In Development, this is also overridable via User Secrets (Lesson 02-C).
builder.Services.AddDbContext<BankingDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("BankDb")
        ?? "Data Source=bank.db"));

// ----- 03-B: Repository registration -----
builder.Services.AddScoped<IAccountRepository, AccountRepository>();

// ----- 03-C: Unit of Work registration -----
// UnitOfWork is Scoped so it shares the same DbContext instance as AccountRepository.
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// ----- 04-A: Customer repository -----
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();

// ----- 05: LINQ service (in-memory, no DB) -----
builder.Services.AddSingleton<Lesson.Services.LinqService>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// ----- Apply pending EF Core migrations automatically at startup -----
// In production you would use a dedicated migration step (e.g. CI/CD pipeline).
// For learning purposes, MigrateAsync() is called here so the DB is always up to date.
// Java parallel: Flyway.migrate() / spring.flyway.enabled=true
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();
