using Lesson.Messaging;

namespace Lesson.HostedServices;

/// <summary>
/// Lesson 08-C — Background consumer using IHostedService / BackgroundService.
///
/// BackgroundService is an abstract base that implements IHostedService and
/// provides a single override point: ExecuteAsync(CancellationToken).
///
/// The host:
///   1. Calls StartAsync → begins ExecuteAsync on a background thread.
///   2. Calls StopAsync when the application shuts down → triggers the cancellation token.
///
/// Java parallel:
///   @KafkaListener / @RabbitListener consumer loop,
///   or a Spring @Scheduled background method.
///   More precisely: a Runnable submitted to a ThreadPoolTaskExecutor
///   that loops until interrupted.
///
/// Key .NET concepts demonstrated:
///   • BackgroundService base class
///   • CancellationToken for graceful shutdown
///   • Channel&lt;T&gt;.Reader.ReadAllAsync — async enumerable over the channel
///   • Processing and recording consumed messages
/// </summary>
public class OutboxConsumerService(
    OutboxChannel channel,
    ILogger<OutboxConsumerService> logger) : BackgroundService
{
    // Visible to tests and the demo controller
    private readonly List<OutboxMessage> _processed = [];
    public IReadOnlyList<OutboxMessage> Processed => _processed.AsReadOnly();
    public static void ClearStatic() { } // tests reset via the controller endpoint

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxConsumerService started.");

        // ReadAllAsync yields each message as it becomes available.
        // The loop exits naturally when the channel is completed (app shutdown).
        // stoppingToken cancels the wait if shutdown happens while the queue is empty.
        await foreach (var message in channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                logger.LogInformation(
                    "Processing outbox message {Id} [{EventType}]: {Payload}",
                    message.Id, message.EventType, message.Payload);

                // Simulate work (DB write, HTTP call, etc.)
                await Task.Delay(5, stoppingToken);

                _processed.Add(message);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // graceful shutdown
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process message {Id}", message.Id);
                // In a real outbox you would mark the record as failed and retry later
            }
        }

        logger.LogInformation("OutboxConsumerService stopped.");
    }
}
