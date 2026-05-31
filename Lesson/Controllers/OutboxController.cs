using System.Text.Json;
using Lesson.HostedServices;
using Lesson.Messaging;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 08-C — Channel&lt;T&gt; + IHostedService demo endpoints.
///
/// The controller is the "producer": it writes to OutboxChannel.
/// OutboxConsumerService is the "consumer": it reads from OutboxChannel in the background.
/// </summary>
[ApiController]
[Route("outbox")]
public class OutboxController(
    OutboxChannel channel,
    OutboxConsumerService consumer) : ControllerBase
{
    // POST /outbox — publish a message to the channel
    [HttpPost]
    public async Task<IActionResult> Publish([FromBody] PublishRequest request)
    {
        var message = new OutboxMessage(
            Id: Guid.NewGuid(),
            EventType: request.EventType,
            Payload: JsonSerializer.Serialize(request.Payload),
            CreatedAt: DateTime.UtcNow);

        await channel.Writer.WriteAsync(message);
        return Accepted(new { message.Id, queued = true });
    }

    // GET /outbox/processed — returns messages already consumed by the background service
    [HttpGet("processed")]
    public IActionResult GetProcessed() =>
        Ok(consumer.Processed.Select(m => new
        {
            m.Id, m.EventType, m.Payload, m.CreatedAt
        }));

    public record PublishRequest(string EventType, object Payload);
}
