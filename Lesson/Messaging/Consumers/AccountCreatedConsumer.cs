using Lesson.Messaging.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Lesson.Messaging.Consumers;

/// <summary>
/// Lesson 17-A — Basic MassTransit consumer.
///
/// A consumer is MassTransit's equivalent of a message listener.
/// Register it with AddConsumer&lt;T&gt; and it will be wired to the in-memory transport
/// (or RabbitMQ/Kafka transport in later parts) automatically.
///
/// Java parallel:
///   @RabbitListener(queues = "account.created")  →  IConsumer&lt;T&gt;
///   void handleMessage(AccountCreatedEvent e)     →  Task Consume(ConsumeContext&lt;T&gt; ctx)
/// </summary>
public class AccountCreatedConsumer(ILogger<AccountCreatedConsumer> logger)
    : IConsumer<AccountCreatedEvent>
{
    public Task Consume(ConsumeContext<AccountCreatedEvent> context)
    {
        var msg = context.Message;
        logger.LogInformation(
            "17-A [AccountCreatedConsumer] Account {Number} (id={Id}) opened by {Owner} " +
            "with balance {Balance:C} at {At}",
            msg.AccountNumber, msg.AccountId, msg.OwnerName, msg.InitialBalance, msg.OccurredAt);

        return Task.CompletedTask;
    }
}
