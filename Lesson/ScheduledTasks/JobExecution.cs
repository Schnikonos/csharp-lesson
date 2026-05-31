namespace Lesson.ScheduledTasks;

/// <summary>
/// Lesson 09-A — Recorded execution of the interest calculation job.
/// Kept in-memory for demo purposes; in production this would be a DB row.
/// </summary>
public record JobExecution(
    Guid RunId,
    DateTime StartedAt,
    DateTime? FinishedAt,
    int AccountsProcessed,
    string Status);           // "Running" | "Completed" | "Failed"
