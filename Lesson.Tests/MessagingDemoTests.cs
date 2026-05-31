using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lesson.Messaging.Events;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lesson.Data;

namespace Lesson.Tests;

/// <summary>
/// Lesson 17-A — MassTransit in-memory bus tests.
///
/// MassTransit ships with a built-in test harness (ITestHarness) that lets you:
///   - Assert a message was published
///   - Assert a consumer was called
///   - Assert a message ended up on an error queue
/// All without a real broker.
///
/// Java parallel:
///   @SpringBootTest + EmbeddedKafka  →  ITestHarness (in-memory)
///   verify(rabbitTemplate).convertAndSend(...)  →  harness.Published.Select&lt;T&gt;()
/// </summary>
public class MessagingDemoTests : IClassFixture<MessagingTestFactory>
{
    private readonly MessagingTestFactory _factory;
    public MessagingDemoTests(MessagingTestFactory factory) => _factory = factory;

    private HttpClient NewClient() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // POST /messaging/account-created returns 202 Accepted
    [Fact]
    public async Task PublishAccountCreated_Returns202()
    {
        var resp = await NewClient().PostAsJsonAsync("/messaging/account-created",
            new { accountId = 1, accountNumber = "MSG-001", ownerName = "Alice", initialBalance = 1000m });

        resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    // Published message appears in the test harness
    [Fact]
    public async Task PublishAccountCreated_MessageAppearsOnBus()
    {
        await NewClient().PostAsJsonAsync("/messaging/account-created",
            new { accountId = 2, accountNumber = "MSG-002", ownerName = "Bob", initialBalance = 500m });

        // Give the in-memory bus a moment to dispatch
        await Task.Delay(200);

        using var scope = _factory.Services.CreateScope();
        var harness = scope.ServiceProvider.GetRequiredService<ITestHarness>();

        var published = harness.Published.Select<AccountCreatedEvent>().ToList();
        published.Should().NotBeEmpty();
        published.Should().Contain(p => p.Context.Message.AccountNumber == "MSG-002");
    }

    // Consumer was invoked
    [Fact]
    public async Task PublishAccountCreated_ConsumerIsInvoked()
    {
        await NewClient().PostAsJsonAsync("/messaging/account-created",
            new { accountId = 3, accountNumber = "MSG-003", ownerName = "Carol", initialBalance = 200m });

        await Task.Delay(300);

        using var scope = _factory.Services.CreateScope();
        var harness = scope.ServiceProvider.GetRequiredService<ITestHarness>();
        var consumerHarness = harness.GetConsumerHarness<Lesson.Messaging.Consumers.AccountCreatedConsumer>();

        (await consumerHarness.Consumed.Any<AccountCreatedEvent>()).Should().BeTrue();
    }

    // Multiple publishes accumulate in harness
    [Fact]
    public async Task PublishAccountCreated_MultipleTimes_AllRecorded()
    {
        var client = NewClient();
        for (int i = 10; i <= 12; i++)
            await client.PostAsJsonAsync("/messaging/account-created",
                new { accountId = i, accountNumber = $"MSG-{i:D3}", ownerName = $"User{i}", initialBalance = (decimal)i * 100 });

        await Task.Delay(400);

        using var scope = _factory.Services.CreateScope();
        var harness = scope.ServiceProvider.GetRequiredService<ITestHarness>();

        harness.Published.Select<AccountCreatedEvent>().Should().HaveCountGreaterThanOrEqualTo(3);
    }
}

public class MessagingTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;
    public MessagingTestFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            var d = services.SingleOrDefault(s => s.ServiceType == typeof(DbContextOptions<BankingDbContext>));
            if (d is not null) services.Remove(d);
            services.AddDbContext<BankingDbContext>(o => o.UseSqlite(_connection));

            // Replace MassTransit in-memory transport with the test harness
            // (AddMassTransitTestHarness re-registers the bus with test instrumentation)
            services.AddMassTransitTestHarness(x =>
            {
                x.AddConsumers(typeof(Program).Assembly);
            });

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            scope.ServiceProvider.GetRequiredService<BankingDbContext>().Database.Migrate();
        });
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
