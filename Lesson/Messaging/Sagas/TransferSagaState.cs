namespace Lesson.Messaging.Sagas;

/// <summary>
/// Saga state — persisted between steps of a long-running workflow.
/// MassTransit uses this class as both the state container and the EF Core entity.
///
/// Java parallel:
///   Axon Framework @Saga / Spring State Machine state  →  SagaStateMachineInstance
/// </summary>
public class TransferSagaState : MassTransit.SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }   // MassTransit saga identity
    public string CurrentState { get; set; } = null!;

    // Business data carried through the saga
    public int  FromAccountId { get; set; }
    public int  ToAccountId   { get; set; }
    public decimal Amount     { get; set; }
    public string? FailureReason { get; set; }
}
