using Lesson.Messaging.Contracts;
using Lesson.UnitOfWork;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Lesson.Messaging.Consumers;

/// <summary>
/// Lesson 17-B — Request/Response consumer.
///
/// Responds to GetAccountBalanceRequest by looking up the account in the DB.
/// The caller uses IRequestClient&lt;GetAccountBalanceRequest&gt; and awaits the response.
///
/// Java parallel:
///   Spring Integration @ServiceActivator replying to a gateway request
///   → IConsumer&lt;T&gt; calling ctx.RespondAsync(response)
/// </summary>
public class GetAccountBalanceConsumer(IUnitOfWork uow, ILogger<GetAccountBalanceConsumer> logger)
    : IConsumer<GetAccountBalanceRequest>
{
    public async Task Consume(ConsumeContext<GetAccountBalanceRequest> context)
    {
        var id      = context.Message.AccountId;
        var account = await uow.Accounts.GetByIdAsync(id);

        logger.LogInformation("17-B [GetAccountBalanceConsumer] Looking up balance for account {Id}", id);

        await context.RespondAsync(account is null
            ? new GetAccountBalanceResponse(id, 0, Found: false)
            : new GetAccountBalanceResponse(id, account.Balance, Found: true));
    }
}
