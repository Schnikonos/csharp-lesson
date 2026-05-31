using Lesson.Messaging.Sagas;
using Lesson.UnitOfWork;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Lesson.Messaging.Sagas;

/// <summary>
/// Lesson 17-C — MassTransit Saga (state machine).
///
/// A saga is a long-running, stateful workflow coordinated through messages.
/// Each state transition is triggered by an incoming message.
///
/// Transfer saga states:
///   Initial  →  (InitiateTransferCommand)  →  Reserving
///   Reserving →  (FundsReservedEvent)       →  Completing
///   Completing → (TransferCompletedEvent)   →  Final
///   Any        → (TransferFailedEvent)      →  Failed (Final)
///
/// Java parallel:
///   Axon Framework @Saga + @SagaEventHandler  →  MassTransitStateMachine
///   Spring State Machine transitions          →  State&lt;T&gt; + Event&lt;T&gt; declarations
/// </summary>
public class TransferStateMachine : MassTransitStateMachine<TransferSagaState>
{
    // ── States ──────────────────────────────────────────────────────────────
    public State Failed { get; private set; } = null!;

    // ── Events (message triggers) ────────────────────────────────────────────
    public Event<InitiateTransferCommand> TransferInitiated { get; private set; } = null!;
    public Event<TransferCompletedEvent>  TransferCompleted { get; private set; } = null!;
    public Event<TransferFailedEvent>     TransferFailed    { get; private set; } = null!;

    public TransferStateMachine()
    {
        // CorrelationId is carried in each message
        InstanceState(x => x.CurrentState);

        Event(() => TransferInitiated,
            x => x.CorrelateById(ctx => ctx.Message.CorrelationId));
        Event(() => TransferCompleted,
            x => x.CorrelateById(ctx => ctx.Message.CorrelationId));
        Event(() => TransferFailed,
            x => x.CorrelateById(ctx => ctx.Message.CorrelationId));

        // ── Transitions ──────────────────────────────────────────────────────
        Initially(
            When(TransferInitiated)
                .Then(ctx =>
                {
                    ctx.Saga.FromAccountId = ctx.Message.FromAccountId;
                    ctx.Saga.ToAccountId   = ctx.Message.ToAccountId;
                    ctx.Saga.Amount        = ctx.Message.Amount;
                })
                // In a real saga you would call an external service here and wait for an async reply.
                // For this lesson we immediately publish TransferCompletedEvent and finalize.
                .PublishAsync(ctx => ctx.Init<TransferCompletedEvent>(new
                {
                    CorrelationId = ctx.Saga.CorrelationId,
                    FromAccountId = ctx.Saga.FromAccountId,
                    ToAccountId   = ctx.Saga.ToAccountId,
                    Amount        = ctx.Saga.Amount,
                }))
                .Finalize());

        // Fault handling from any state
        DuringAny(
            When(TransferFailed)
                .Then(ctx => ctx.Saga.FailureReason = ctx.Message.Reason)
                .TransitionTo(Failed)
                .Finalize());

        SetCompletedWhenFinalized();
    }
}
