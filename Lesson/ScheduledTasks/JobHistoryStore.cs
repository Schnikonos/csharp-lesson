namespace Lesson.ScheduledTasks;

/// <summary>
/// Lesson 09-A — In-memory store for job execution history.
/// Singleton, shared between the background service and the demo controller.
/// </summary>
public sealed class JobHistoryStore
{
    private readonly List<JobExecution> _history = [];
    public IReadOnlyList<JobExecution> History => _history.AsReadOnly();

    public void Add(JobExecution run) => _history.Add(run);
    public void Clear() => _history.Clear();

    public void Update(Guid runId, JobExecution updated)
    {
        var idx = _history.FindIndex(r => r.RunId == runId);
        if (idx >= 0) _history[idx] = updated;
    }
}
