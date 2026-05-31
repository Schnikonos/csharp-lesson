using Lesson.ScheduledTasks;

namespace Lesson.HostedServices;

/// <summary>
/// Lesson 09-A — PeriodicTimer-based background service.
///
/// PeriodicTimer (introduced in .NET 6) is the modern, cancellation-friendly
/// replacement for Timer + Task.Delay loops.
///
/// Key advantages over older approaches:
///   • PeriodicTimer.WaitForNextTickAsync suspends without blocking a thread.
///   • The timer ticks are NOT queued — if a tick is missed (job runs longer
///     than the interval) the next tick fires immediately once. This prevents
///     unbounded work accumulation (unlike System.Threading.Timer).
///   • CancellationToken integration is first-class.
///
/// Java parallel:
///   @Scheduled(fixedDelay = 60_000) on a @Component method — Spring's
///   ThreadPoolTaskScheduler calls the method; period here is simulated via
///   PeriodicTimer.WaitForNextTickAsync.
///
/// The simulated job: "calculate daily interest on all savings accounts"
/// (in-memory simulation — no real DB writes to keep the lesson focused on scheduling).
/// </summary>
public class InterestCalculationService(
    JobHistoryStore store,
    ILogger<InterestCalculationService> logger,
    TimeSpan? period = null)          // injectable for tests (default: 10 s)
    : BackgroundService
{
    // In tests we inject a very short period so the timer fires at least once quickly.
    private readonly TimeSpan _period = period ?? TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("InterestCalculationService started (period {Period}).", _period);

        // PeriodicTimer — fires on the given period; WaitForNextTickAsync returns false
        // when the token is cancelled, allowing a clean loop exit.
        using var timer = new PeriodicTimer(_period);

        while (await timer.WaitForNextTickAsync(stoppingToken))
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
                logger.LogInformation("Interest calculation starting (run {RunId}).", run.RunId);

                // Simulated work: pretend we fetched and updated N savings accounts.
                var processed = Random.Shared.Next(1, 20);
                await Task.Delay(TimeSpan.FromMilliseconds(50), stoppingToken);

                // Update the record (records are immutable — replace via index)
                var completed = run with
                {
                    FinishedAt = DateTime.UtcNow,
                    AccountsProcessed = processed,
                    Status = "Completed"
                };

                ReplaceExecution(run.RunId, completed);
                logger.LogInformation(
                    "Interest calculation completed: {Accounts} accounts processed.", processed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                ReplaceExecution(run.RunId, run with { Status = "Cancelled", FinishedAt = DateTime.UtcNow });
                break;
            }
            catch (Exception ex)
            {
                ReplaceExecution(run.RunId, run with { Status = "Failed", FinishedAt = DateTime.UtcNow });
                logger.LogError(ex, "Interest calculation failed.");
            }
        }

        logger.LogInformation("InterestCalculationService stopped.");
    }

    private void ReplaceExecution(Guid runId, JobExecution updated) =>
        store.Update(runId, updated);
}
