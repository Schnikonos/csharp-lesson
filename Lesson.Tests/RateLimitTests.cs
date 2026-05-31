using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Lesson.Data;

namespace Lesson.Tests;

// =============================================================================
// LESSON 06-C EXTENDED TESTS: Built-in Rate Limiting
//
// Strategy: spin up the full ASP.NET Core pipeline via WebApplicationFactory
// and fire requests until the 429 threshold is crossed.
//
// Three scenarios, one per algorithm:
//   1. Fixed window   — burst N+1 requests, last one returns 429.
//   2. Sliding window — same burst, same expectation (algorithm differs but
//                       limit is the same for test determinism).
//   3. Token bucket   — exhaust the initial token grant, expect 429.
//
// IMPORTANT: each test creates its own factory so the in-memory rate-limiter
// counters are isolated between tests.
//
// Java parallel:
//   Resilience4j RateLimiter / Bucket4j integration test
//   RateLimiterRegistry.of(config) → checked inside @SpringBootTest
// =============================================================================

public class RateLimitTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public RateLimitTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    // =========================================================================
    // TEST 1 — Fixed window: 11th request within the same window → 429
    // =========================================================================
    [Fact]
    public async Task FixedWindow_ExceedsLimit_Returns429()
    {
        using var factory = BuildFactory();
        var client = factory.CreateClient();

        // Fire PermitLimit (10) successful requests
        for (var i = 0; i < 10; i++)
        {
            var r = await client.GetAsync("/rate-demo/fixed");
            r.StatusCode.Should().Be(HttpStatusCode.OK, $"request {i + 1} should be within limit");
        }

        // 11th request must be rejected
        var rejected = await client.GetAsync("/rate-demo/fixed");
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // =========================================================================
    // TEST 2 — Sliding window: 11th request within the first sub-window → 429
    // =========================================================================
    [Fact]
    public async Task SlidingWindow_ExceedsLimit_Returns429()
    {
        using var factory = BuildFactory();
        var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            var r = await client.GetAsync("/rate-demo/sliding");
            r.StatusCode.Should().Be(HttpStatusCode.OK, $"request {i + 1} should be within limit");
        }

        var rejected = await client.GetAsync("/rate-demo/sliding");
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // =========================================================================
    // TEST 3 — Token bucket: exhaust the initial grant (TokenLimit = 20) → 429
    // =========================================================================
    [Fact]
    public async Task TokenBucket_ExceedsInitialGrant_Returns429()
    {
        using var factory = BuildFactory();
        var client = factory.CreateClient();

        // Consume all 20 tokens
        for (var i = 0; i < 20; i++)
        {
            var r = await client.GetAsync("/rate-demo/token");
            r.StatusCode.Should().Be(HttpStatusCode.OK, $"token {i + 1} should be available");
        }

        // No tokens left → rejected
        var rejected = await client.GetAsync("/rate-demo/token");
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    // =========================================================================
    // TEST 4 — Requests under the limit always succeed
    // =========================================================================
    [Fact]
    public async Task UnderLimit_AllRequestsSucceed()
    {
        using var factory = BuildFactory();
        var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            var r = await client.GetAsync("/rate-demo/fixed");
            r.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: isolated factory with in-memory SQLite (no shared DB state)
    // ─────────────────────────────────────────────────────────────────────────
    private WebApplicationFactory<Program> BuildFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.ConfigureServices(services =>
            {
                // Replace EF Core registration with the in-memory SQLite connection
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<BankingDbContext>));
                if (descriptor is not null) services.Remove(descriptor);

                services.AddDbContext<BankingDbContext>(opts =>
                    opts.UseSqlite(_connection));
            }));
}
