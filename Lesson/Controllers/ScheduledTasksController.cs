using Lesson.ScheduledTasks;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 09-A — Exposes job execution history for demo / testing.
/// The background service runs independently; this controller just lets us
/// inspect and reset the history store.
/// </summary>
[ApiController]
[Route("scheduled-tasks")]
public class ScheduledTasksController(JobHistoryStore store) : ControllerBase
{
    // GET /scheduled-tasks/history — list all recorded runs
    [HttpGet("history")]
    public IActionResult GetHistory() =>
        Ok(store.History.Select(r => new
        {
            r.RunId,
            r.StartedAt,
            r.FinishedAt,
            r.AccountsProcessed,
            r.Status
        }));

    // DELETE /scheduled-tasks/history/reset — test helper
    [HttpDelete("history/reset")]
    public IActionResult Reset()
    {
        store.Clear();
        return NoContent();
    }
}
