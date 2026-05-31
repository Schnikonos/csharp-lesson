using FluentAssertions;
using Lesson.Domain;
using Lesson.Entities;
using Lesson.Repositories;
using Lesson.UnitOfWork;
using Moq;

namespace Lesson.Tests;

/// <summary>
/// Lesson 12-B — Moq + FluentAssertions.
///
/// These tests replace real infrastructure (database, HTTP clients) with
/// mock objects so business logic can be tested in complete isolation.
///
/// Key concepts:
///   Mock&lt;T&gt;        — creates a test double that records calls
///   Setup(...)     — defines return values / behaviours
///   Verify(...)    — asserts a method WAS (or was NOT) called
///   FluentAssertions — readable assertion chains: .Should().Be(), .BeNull(), etc.
///
/// Java parallel:
///   Mockito.mock(T)       → new Mock&lt;T&gt;()
///   when(m.x()).thenReturn → mock.Setup(m => m.x()).Returns(...)
///   verify(m).x()         → mock.Verify(m => m.x(), Times.Once())
///   assertThat(v).isEqualTo(expected) → v.Should().Be(expected)
/// </summary>
public class MockedAccountRepositoryTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────
    private static BankAccount MakeAccount(int id = 1, decimal balance = 500m) =>
        new()
        {
            Id            = id,
            AccountNumber = $"BANK-{id:D4}",
            OwnerName     = "Bob",
            Balance       = balance,
            IsActive      = true
        };

    // ── GetByIdAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ReturnsAccount_WhenExists()
    {
        // Arrange
        var expected = MakeAccount(id: 42, balance: 1_200m);
        var mockRepo = new Mock<IAccountRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(42)).ReturnsAsync(expected);

        // Act
        var result = await mockRepo.Object.GetByIdAsync(42);

        // Assert — FluentAssertions chained style
        result.Should().NotBeNull();
        result!.Id.Should().Be(42);
        result.Balance.Should().Be(1_200m);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotExists()
    {
        var mockRepo = new Mock<IAccountRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((BankAccount?)null);

        var result = await mockRepo.Object.GetByIdAsync(999);

        result.Should().BeNull();
    }

    // ── ExistsAsync ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistsAsync_ReturnsMockedValue(bool exists)
    {
        var mockRepo = new Mock<IAccountRepository>();
        mockRepo.Setup(r => r.ExistsAsync("ACC-001")).ReturnsAsync(exists);

        var result = await mockRepo.Object.ExistsAsync("ACC-001");

        result.Should().Be(exists);
    }

    // ── Verify call counts ────────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_IsCalledOnce_WhenAccountCreated()
    {
        // Arrange
        var account  = MakeAccount();
        var mockRepo = new Mock<IAccountRepository>();
        mockRepo.Setup(r => r.AddAsync(account)).ReturnsAsync(account);

        // Act
        await mockRepo.Object.AddAsync(account);

        // Assert — Moq Verify: was AddAsync called exactly once with this account?
        // Java parallel: verify(repo, times(1)).save(account)
        mockRepo.Verify(r => r.AddAsync(account), Times.Once());
    }

    [Fact]
    public async Task GetByIdAsync_NeverCalledWithZero_ByDefault()
    {
        var mockRepo = new Mock<IAccountRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync(MakeAccount());

        await mockRepo.Object.GetByIdAsync(5);

        // Verify it was NOT called with id = 0
        mockRepo.Verify(r => r.GetByIdAsync(0), Times.Never());
    }

    // ── UnitOfWork mock ───────────────────────────────────────────────────────

    [Fact]
    public async Task CommitAsync_IsCalled_AfterAddAsync()
    {
        // Arrange — mock the whole unit of work
        var mockRepo = new Mock<IAccountRepository>();
        var mockUow  = new Mock<IUnitOfWork>();
        mockUow.Setup(u => u.Accounts).Returns(mockRepo.Object);
        mockUow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var account = MakeAccount();

        // Act — simulate what a service layer would do
        await mockUow.Object.Accounts.AddAsync(account);
        await mockUow.Object.CommitAsync();

        // Assert
        mockRepo.Verify(r => r.AddAsync(account), Times.Once());
        mockUow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    // ── FluentAssertions collection assertions ────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnedList_HasExpectedProperties()
    {
        var accounts = new List<BankAccount>
        {
            MakeAccount(1, 100m),
            MakeAccount(2, 200m),
            MakeAccount(3, 300m),
        };
        var mockRepo = new Mock<IAccountRepository>();
        mockRepo.Setup(r => r.GetAllAsync(null)).ReturnsAsync(accounts);

        var result = (await mockRepo.Object.GetAllAsync(null)).ToList();

        // FluentAssertions — readable collection assertions
        result.Should().HaveCount(3);
        result.Should().AllSatisfy(a => a.IsActive.Should().BeTrue());
        result.Select(a => a.Balance).Should().BeInAscendingOrder();
        result.Should().Contain(a => a.Id == 2);
    }

    // ── BankAccountDomainService + mock together ──────────────────────────────

    [Fact]
    public void DomainService_Deposit_And_FluentAssertions_Together()
    {
        var svc     = new BankAccountDomainService();
        var account = MakeAccount(balance: 1_000m);

        svc.Deposit(account, 500m);

        account.Balance.Should().Be(1_500m,
            because: "a 500 deposit on a 1000 balance should equal 1500");
    }

    [Fact]
    public void DomainService_Withdraw_TooMuch_ThrowsWithMessage()
    {
        var svc     = new BankAccountDomainService();
        var account = MakeAccount(balance: 100m);

        // FluentAssertions exception assertion
        var act = () => svc.Withdraw(account, 999m);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("Insufficient funds.");
    }
}
