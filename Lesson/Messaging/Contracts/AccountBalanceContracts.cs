namespace Lesson.Messaging.Contracts;

/// <summary>
/// Request/Response contract pair for MassTransit's IRequestClient&lt;T&gt; pattern.
///
/// This is a synchronous-style RPC over the bus — the caller awaits the response.
/// MassTransit creates a temporary reply queue and correlates the response automatically.
///
/// Java parallel:
///   RabbitTemplate.convertSendAndReceive() / Spring Integration Gateway  →  IRequestClient&lt;T&gt;
/// </summary>
public record GetAccountBalanceRequest(int AccountId);

public record GetAccountBalanceResponse(int AccountId, decimal Balance, bool Found);
