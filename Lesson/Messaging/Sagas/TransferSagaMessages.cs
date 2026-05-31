namespace Lesson.Messaging.Sagas;

// ── Messages that drive the transfer saga ──────────────────────────────────

/// <summary>
/// Command — initiates a transfer saga.
/// Sent by the controller; the saga is created when it receives this message.
/// </summary>
public record InitiateTransferCommand(
    Guid    CorrelationId,
    int     FromAccountId,
    int     ToAccountId,
    decimal Amount);

/// <summary>Emitted by the saga after funds are reserved on the source account.</summary>
public record FundsReservedEvent(Guid CorrelationId, int FromAccountId, decimal Amount);

/// <summary>Emitted by the saga when the transfer is fully completed.</summary>
public record TransferCompletedEvent(Guid CorrelationId, int FromAccountId, int ToAccountId, decimal Amount);

/// <summary>Emitted when the transfer fails (insufficient funds or account not found).</summary>
public record TransferFailedEvent(Guid CorrelationId, string Reason);
