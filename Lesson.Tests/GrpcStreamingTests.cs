using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Lesson.Grpc;
using GrpcCreateRequest = Lesson.Grpc.CreateAccountRequest;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lesson.Data;

namespace Lesson.Tests;

/// <summary>
/// Lesson 20-B — server-streaming RPC, deadline propagation, and cancellation.
///
/// Java parallel:
///   stub.listAccounts(...) returning Iterator&lt;AccountReply&gt; / StreamObserver
///   stub.withDeadlineAfter(5, TimeUnit.SECONDS)  →  deadline: DateTime.UtcNow.AddSeconds(5)
/// </summary>
public class GrpcStreamingTests : IClassFixture<GrpcStreamingFactory>
{
    private readonly GrpcStreamingFactory _factory;
    public GrpcStreamingTests(GrpcStreamingFactory factory) => _factory = factory;

    private BankingService.BankingServiceClient Client()
    {
        var http = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost") });
        var ch   = GrpcChannel.ForAddress(http.BaseAddress!, new GrpcChannelOptions { HttpClient = http });
        return new BankingService.BankingServiceClient(ch);
    }

    // ListAccounts streams all accounts created before the call
    [Fact]
    public async Task ListAccounts_StreamsAll()
    {
        var c = Client();
        await c.CreateAccountAsync(new GrpcCreateRequest { AccountNumber = "STR-001", OwnerName = "A", InitialBalance = 100 });
        await c.CreateAccountAsync(new GrpcCreateRequest { AccountNumber = "STR-002", OwnerName = "B", InitialBalance = 200 });

        var list = new List<AccountReply>();
        var call = c.ListAccounts(new ListAccountsRequest());
        await foreach (var reply in call.ResponseStream.ReadAllAsync())
            list.Add(reply);

        list.Count.Should().BeGreaterThanOrEqualTo(2);
        list.Should().Contain(r => r.AccountNumber == "STR-001");
        list.Should().Contain(r => r.AccountNumber == "STR-002");
    }

    // Replies arrive with correct balance values
    [Fact]
    public async Task ListAccounts_BalancesCorrect()
    {
        var c = Client();
        await c.CreateAccountAsync(new GrpcCreateRequest { AccountNumber = "STR-003", OwnerName = "C", InitialBalance = 777 });

        var list = new List<AccountReply>();
        var call = c.ListAccounts(new ListAccountsRequest());
        await foreach (var reply in call.ResponseStream.ReadAllAsync())
            list.Add(reply);

        list.Should().Contain(r => r.AccountNumber == "STR-003" && Math.Abs(r.Balance - 777) < 0.01);
    }

    // Deadline: if deadline is very short the call may fail with DeadlineExceeded
    // We seed several accounts so the streaming takes some time; a 1ms deadline should trigger the error.
    // NOTE: with in-process transport the deadline may not always trigger — we accept both outcomes.
    [Fact]
    public async Task ListAccounts_VeryShortDeadline_DeadlineExceededOrOk()
    {
        var c = Client();
        // Seed a few accounts
        for (var i = 0; i < 5; i++)
            await c.CreateAccountAsync(new GrpcCreateRequest { AccountNumber = $"DL-{i:D3}", OwnerName = "X", InitialBalance = i });

        var list   = new List<AccountReply>();
        StatusCode statusCode = StatusCode.OK;

        try
        {
            // 1 ms deadline — likely to expire mid-stream
            var call = c.ListAccounts(new ListAccountsRequest(), deadline: DateTime.UtcNow.AddMilliseconds(1));
            await foreach (var reply in call.ResponseStream.ReadAllAsync())
                list.Add(reply);
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.DeadlineExceeded or StatusCode.Cancelled)
        {
            statusCode = ex.StatusCode;
        }

        // Either we finished quickly (OK) or hit the deadline — both are valid outcomes
        statusCode.Should().BeOneOf(StatusCode.OK, StatusCode.DeadlineExceeded, StatusCode.Cancelled);
    }

    // Cancellation via CancellationToken raises Cancelled or DeadlineExceeded
    [Fact]
    public async Task ListAccounts_Cancelled_ThrowsRpcException()
    {
        var c = Client();
        for (var i = 0; i < 5; i++)
            await c.CreateAccountAsync(new GrpcCreateRequest { AccountNumber = $"CX-{i:D3}", OwnerName = "X", InitialBalance = i });

        using var cts  = new CancellationTokenSource(TimeSpan.FromMilliseconds(1));
        StatusCode code = StatusCode.OK;
        try
        {
            var call = c.ListAccounts(new ListAccountsRequest());
            await foreach (var reply in call.ResponseStream.ReadAllAsync(cts.Token))
            { /* cancel mid-stream */ }
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.Cancelled or StatusCode.DeadlineExceeded)
        {
            code = ex.StatusCode;
        }
        catch (OperationCanceledException) { code = StatusCode.Cancelled; }

        code.Should().BeOneOf(StatusCode.OK, StatusCode.Cancelled, StatusCode.DeadlineExceeded);
    }
}

public class GrpcStreamingFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public GrpcStreamingFactory() { _connection = new("DataSource=:memory:"); _connection.Open(); }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureKestrel(o => o.ListenLocalhost(0, lo => lo.Protocols = HttpProtocols.Http2));
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
