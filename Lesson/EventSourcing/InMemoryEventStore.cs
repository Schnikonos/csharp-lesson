using System.Collections.Concurrent;

namespace Lesson.EventSourcing;

/// <summary>
/// Simple in-memory append-only event store.
/// In production this would be EventStoreDB, Marten, or a custom SQL append table.
/// </summary>
public interface IEventStore
{
    Task AppendAsync(Guid aggregateId, IEnumerable<IEvent> events, CancellationToken ct = default);
    Task<IReadOnlyList<IEvent>> LoadAsync(Guid aggregateId, CancellationToken ct = default);
}

public class InMemoryEventStore : IEventStore
{
    private readonly ConcurrentDictionary<Guid, List<IEvent>> _streams = new();

    public Task AppendAsync(Guid aggregateId, IEnumerable<IEvent> events, CancellationToken ct = default)
    {
        var stream = _streams.GetOrAdd(aggregateId, _ => []);
        lock (stream)
            stream.AddRange(events);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<IEvent>> LoadAsync(Guid aggregateId, CancellationToken ct = default)
    {
        if (_streams.TryGetValue(aggregateId, out var stream))
        {
            lock (stream)
                return Task.FromResult<IReadOnlyList<IEvent>>([.. stream]);
        }
        return Task.FromResult<IReadOnlyList<IEvent>>([]);
    }
}
