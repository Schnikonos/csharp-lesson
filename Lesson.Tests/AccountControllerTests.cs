using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Lesson.DTOs;
using Lesson.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Lesson.Tests;

// =============================================================================
// INTEGRATION TEST: AccountController
//
// WHAT WE ARE TESTING:
//   The full HTTP pipeline: routing → [ApiController] validation →
//   controller → service → response serialization.
//
// THE TOOL — WebApplicationFactory<TProgram>:
//   Boots the real ASP.NET Core app in-memory (no listening TCP port).
//   Creates an HttpClient that talks to this in-memory server.
//   You can override any DI registration to replace real services with mocks.
//
// Java parallel:
//   @SpringBootTest(webEnvironment = RANDOM_PORT) + TestRestTemplate
//   → WebApplicationFactory<Program> + CreateClient()
//
//   @MockBean IAccountService mockService
//   → WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton(mockService)))
//
// TEST TYPES IN THIS FILE:
//   - Integration tests (real HTTP pipeline, in-memory server)
//   - IAccountService is mocked so tests are fast and deterministic
// =============================================================================

public class AccountControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    // -------------------------------------------------------------------------
    // C# NOTE: IClassFixture<T>
    //   xUnit creates one WebApplicationFactory instance shared across all tests
    //   in this class. Equivalent to @BeforeAll in JUnit 5.
    //   The factory (and the in-memory server) is created once and reused.
    // -------------------------------------------------------------------------
    private readonly WebApplicationFactory<Program> _factory;

    public AccountControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // Helper — creates a client with IAccountService replaced by a mock
    private (HttpClient Client, Mock<IAccountService> ServiceMock) CreateClientWithMock()
    {
        var mockService = new Mock<IAccountService>();

        var client = _factory
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Remove the real Singleton registration and replace with mock.
                    // This is the in-process equivalent of @MockBean in Spring Test.
                    services.AddSingleton(mockService.Object);
                });
            })
            .CreateClient();

        return (client, mockService);
    }

    // =========================================================================
    // TEST 1 — GET /account returns 200 with list of accounts
    // =========================================================================
    [Fact]
    public async Task GetAll_Returns200_WithAccounts()
    {
        // ----- Arrange -------------------------------------------------------
        var seedAccounts = new List<AccountResponse>
        {
            new(Guid.NewGuid(), "Alice Martin", "FR76300060000112345678", 1000m, "EUR"),
            new(Guid.NewGuid(), "Bob Dupont",   "DE89370400440532013000",  500m, "EUR")
        };

        var (client, mockService) = CreateClientWithMock();
        mockService.Setup(s => s.GetAll()).Returns(seedAccounts);

        // ----- Act -----------------------------------------------------------
        var response = await client.GetAsync("/account");

        // ----- Assert --------------------------------------------------------
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var accounts = await response.Content.ReadFromJsonAsync<List<AccountResponse>>();
        accounts.Should().HaveCount(2);
        accounts![0].Owner.Should().Be("Alice Martin");
    }

    // =========================================================================
    // TEST 2 — GET /account/{id} with valid id returns 200
    // =========================================================================
    [Fact]
    public async Task GetById_WhenFound_Returns200()
    {
        // ----- Arrange -------------------------------------------------------
        var id = Guid.NewGuid();
        var account = new AccountResponse(id, "Alice Martin", "FR76300060000112345678", 1000m, "EUR");

        var (client, mockService) = CreateClientWithMock();
        mockService.Setup(s => s.GetById(id)).Returns(account);

        // ----- Act -----------------------------------------------------------
        var response = await client.GetAsync($"/account/{id}");

        // ----- Assert --------------------------------------------------------
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AccountResponse>();
        result!.Id.Should().Be(id);
    }

    // =========================================================================
    // TEST 3 — GET /account/{id} with unknown id returns 404
    // =========================================================================
    [Fact]
    public async Task GetById_WhenNotFound_Returns404()
    {
        // ----- Arrange -------------------------------------------------------
        var (client, mockService) = CreateClientWithMock();
        mockService.Setup(s => s.GetById(It.IsAny<Guid>())).Returns((AccountResponse?)null);

        // ----- Act -----------------------------------------------------------
        var response = await client.GetAsync($"/account/{Guid.NewGuid()}");

        // ----- Assert --------------------------------------------------------
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // =========================================================================
    // TEST 4 — POST /account with valid body returns 201 + Location header
    // =========================================================================
    [Fact]
    public async Task Create_WithValidRequest_Returns201WithLocation()
    {
        // ----- Arrange -------------------------------------------------------
        var newId = Guid.NewGuid();
        var created = new AccountResponse(newId, "Carol Smith", "GB29NWBK60161331926819", 250m, "GBP");

        var (client, mockService) = CreateClientWithMock();
        mockService
            .Setup(s => s.Create(It.IsAny<CreateAccountRequest>()))
            .Returns(created);

        var request = new CreateAccountRequest("Carol Smith", "GB29NWBK60161331926819", 250m, "GBP");

        // ----- Act -----------------------------------------------------------
        var response = await client.PostAsJsonAsync("/account", request);

        // ----- Assert --------------------------------------------------------
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain(newId.ToString());
    }

    // =========================================================================
    // TEST 5 — POST /account with missing Owner returns 400 (validation)
    // =========================================================================
    [Fact]
    public async Task Create_WithMissingOwner_Returns400()
    {
        // ----- Arrange -------------------------------------------------------
        // [ApiController] + Data Annotations handle this automatically.
        // The service mock is never called — the pipeline short-circuits at validation.
        var (client, _) = CreateClientWithMock();

        // Send a JSON body with an empty Owner — violates [Required] + [StringLength(min=2)]
        var invalidRequest = new { Owner = "", Iban = "FR76300060000112345678", InitialBalance = 100m, Currency = "EUR" };

        // ----- Act -----------------------------------------------------------
        var response = await client.PostAsJsonAsync("/account", invalidRequest);

        // ----- Assert --------------------------------------------------------
        // C# NOTE: [ApiController] returns 400 + ValidationProblemDetails automatically.
        // No manual ModelState.IsValid check in the controller needed.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
