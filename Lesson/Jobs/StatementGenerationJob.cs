using Lesson.ScheduledTasks;
using Quartz;

namespace Lesson.Jobs;

/// <summary>
/// Lesson 09-B — Quartz.NET IJob implementation.
///
/// Quartz.NET is the .NET equivalent of Spring's @Scheduled + Quartz integration.
///
/// Key Quartz concepts:
///   IJob        — a unit of work (Java: implements org.quartz.Job)
///   IJobDetail  — describes the job class + optional data map
///   ITrigger    — defines when the job runs (cron, simple, calendar)
///   IScheduler  — the engine that fires triggers and executes jobs
///
/// Quartz.NET + DI (Quartz.Extensions.DependencyInjection):
///   Jobs are resolved from the DI container per execution — meaning they
///   can receive scoped services via constructor injection.
///
/// Java parallel:
///   @DisallowConcurrentExecution + implements Job + @Autowired fields
///   Here we use [DisallowConcurrentExecution] attribute on the class.
/// </summary>
[DisallowConcurrentExecution]  // Prevents a new execution if the previous one is still running
public class StatementGenerationJob(
    JobHistoryStore store,
    ILogger<StatementGenerationJob> logger) : IJob
{
    // JobKey is the identity of this job in the scheduler
    public static readonly JobKey Key = new("StatementGeneration", "Banking");

    public async Task Execute(IJobExecutionContext context)
    {
        var run = new JobExecution(
            RunId: Guid.NewGuid(),
            StartedAt: DateTime.UtcNow,
            FinishedAt: null,
            AccountsProcessed: 0,
            Status: "Running");

        store.Add(run);

        try
        {
            logger.LogInformation(
                "StatementGenerationJob started (fire time: {FireTime})",
                context.FireTimeUtc);

            // Simulated: generate PDF statements for all accounts
            var count = Random.Shared.Next(1, 50);
            await Task.Delay(20, context.CancellationToken);

            store.Update(run.RunId, run with
            {
                FinishedAt = DateTime.UtcNow,
                AccountsProcessed = count,
                Status = "Completed"
            });

            logger.LogInformation(
                "StatementGenerationJob completed: {Count} statements generated.", count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            store.Update(run.RunId, run with { Status = "Failed", FinishedAt = DateTime.UtcNow });
            logger.LogError(ex, "StatementGenerationJob failed.");
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
