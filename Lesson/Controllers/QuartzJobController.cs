using Lesson.Jobs;
using Lesson.ScheduledTasks;
using Microsoft.AspNetCore.Mvc;
using Quartz;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 09-B — Quartz.NET demo: manually trigger a job and inspect history.
/// </summary>
[ApiController]
[Route("quartz")]
public class QuartzJobController(ISchedulerFactory schedulerFactory, JobHistoryStore store)
    : ControllerBase
{
    // POST /quartz/trigger — manually fires StatementGenerationJob immediately
    [HttpPost("trigger")]
    public async Task<IActionResult> TriggerNow(CancellationToken ct)
    {
        var scheduler = await schedulerFactory.GetScheduler(ct);
        await scheduler.TriggerJob(StatementGenerationJob.Key, ct);
        return Accepted(new { triggered = true, jobKey = StatementGenerationJob.Key.ToString() });
    }

    // GET /quartz/history — list all recorded runs
    [HttpGet("history")]
    public IActionResult GetHistory() =>
        Ok(store.History.Select(r => new
        {
            r.RunId, r.StartedAt, r.FinishedAt, r.AccountsProcessed, r.Status
        }));

    // DELETE /quartz/history/reset — test helper
    [HttpDelete("history/reset")]
    public IActionResult Reset()
    {
        store.Clear();
        return NoContent();
    }
}
