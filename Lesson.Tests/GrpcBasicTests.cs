using System.Net;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Lesson.Grpc;
using GrpcCreateRequest = Lesson.Grpc.CreateAccountRequest;
using GrpcGetRequest    = Lesson.Grpc.GetAccountRequest;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lesson.Data;

namespace Lesson.Tests;

/// <summary>
/// Lesson 20-A — gRPC unary RPC tests.
///
/// The test client connects via an in-process GrpcChannel backed by the
/// WebApplicationFactory HTTP handler (no real socket needed).
///
/// Java parallel:
///   @SpringBootTest(webEnvironment = DEFINED_PORT) + ManagedChannelBuilder.forAddress(...)
///   io.grpc.testing.GrpcServerRule or GrpcCleanupRule
/// </summary>
public class GrpcBasicTests : IClassFixture<GrpcBasicFactory>
{
    private readonly GrpcBasicFactory _factory;
    public GrpcBasicTests(GrpcBasicFactory factory) => _factory = factory;

    private BankingService.BankingServiceClient Client()
    {
        var httpClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
        });
        var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient,
        });
        return new BankingService.BankingServiceClient(channel);
    }

    // CreateAccount returns the new account with id > 0
    [Fact]
    public async Task CreateAccount_ReturnsCreatedAccount()
    {
        var reply = await Client().CreateAccountAsync(new GrpcCreateRequest
        {
            AccountNumber  = "GRP-001",
            OwnerName      = "Alice",
            InitialBalance = 1000.0,
        });

        reply.Id.Should().BeGreaterThan(0);
        reply.AccountNumber.Should().Be("GRP-001");
        reply.Balance.Should().BeApproximately(1000.0, 0.01);
    }

    // GetAccount returns the account after it was created
    [Fact]
    public async Task GetAccount_ReturnsAccount()
    {
        var created = await Client().CreateAccountAsync(new GrpcCreateRequest
        {
            AccountNumber  = "GRP-002",
            OwnerName      = "Bob",
            InitialBalance = 500.0,
        });

        var reply = await Client().GetAccountAsync(new GrpcGetRequest { Id = created.Id });
        reply.OwnerName.Should().Be("Bob");
    }

    // GetAccount for unknown id throws RpcException with NotFound
    [Fact]
    public async Task GetAccount_Unknown_ThrowsNotFound()
    {
        var act = async () => await Client().GetAccountAsync(new GrpcGetRequest { Id = 99999 });
        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    // CreateAccount twice gives two different ids
    [Fact]
    public async Task CreateAccount_TwiceGivesDifferentIds()
    {
        var r1 = await Client().CreateAccountAsync(new GrpcCreateRequest { AccountNumber = "GRP-003", OwnerName = "C", InitialBalance = 1 });
        var r2 = await Client().CreateAccountAsync(new GrpcCreateRequest { AccountNumber = "GRP-004", OwnerName = "D", InitialBalance = 1 });
        r1.Id.Should().NotBe(r2.Id);
    }
}

public class GrpcBasicFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public GrpcBasicFactory() { _connection = new("DataSource=:memory:"); _connection.Open(); }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // gRPC requires HTTP/2 — configure Kestrel to accept HTTP/2 on the test port
        builder.ConfigureKestrel(o =>
        {
            o.ListenLocalhost(0, lo => lo.Protocols = HttpProtocols.Http2);
        });

        builder.ConfigureServices(services =>
        {
            var d = services.SingleOrDefault(s => s.ServiceType == typeof(DbContextOptions<BankingDbContext>));
            if (d is not null) services.Remove(d);
            services.AddDbContext<BankingDbContext>(o => o.UseSqlite(_connection));
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            scope.ServiceProvider.GetRequiredService<BankingDbContext>().Database.Migrate();
        });
    }

    protected override void Dispose(bool d) { base.Dispose(d); if (d) _connection.Dispose(); }
}
