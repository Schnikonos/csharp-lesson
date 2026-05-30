namespace Lesson.Messaging;

/// <summary>
/// Lesson 08-C — Outbox-style message for async in-process processing.
///
/// In a real outbox pattern this would be persisted to a DB table
/// before being picked up by the consumer. Here we keep it in-memory
/// to focus on Channel&lt;T&gt; and IHostedService mechanics.
///
/// Java parallel: a Kafka/RabbitMQ message record; here the "broker"
/// is System.Threading.Channels.Channel&lt;T&gt;.
/// </summary>
public record OutboxMessage(
    Guid Id,
    string EventType,
    string Payload,
    DateTime CreatedAt);
