using System.Threading.Channels;

namespace Lesson.Messaging;

/// <summary>
/// Lesson 08-C — Async in-process message queue backed by Channel&lt;T&gt;.
///
/// Channel&lt;T&gt; (System.Threading.Channels) is a thread-safe, lock-free
/// producer/consumer queue introduced in .NET Core 3.0.
///
/// BoundedChannel   — fixed capacity; producer blocks/drops when full
/// UnboundedChannel — unlimited capacity; producer never waits
///
/// For learning purposes we use UnboundedChannel here.
///
/// Java parallel:
///   BlockingQueue&lt;T&gt; / LinkedBlockingQueue — producer calls put(), consumer calls take().
///   Channel&lt;T&gt; is more ergonomic for async/await code.
/// </summary>
public sealed class OutboxChannel
{
    // UnboundedChannelOptions — producer never waits; consumer drains at its own pace.
    // SingleReader = true tells the runtime only one consumer exists → small perf win.
    private readonly Channel<OutboxMessage> _channel =
        Channel.CreateUnbounded<OutboxMessage>(
            new UnboundedChannelOptions { SingleReader = true });

    public ChannelWriter<OutboxMessage> Writer  => _channel.Writer;
    public ChannelReader<OutboxMessage> Reader  => _channel.Reader;
}
