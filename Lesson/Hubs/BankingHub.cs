using Microsoft.AspNetCore.SignalR;

namespace Lesson.Hubs;

/// <summary>
/// Lesson 24-A — Typed SignalR Hub.
///
/// A Hub is the server-side endpoint that clients connect to over WebSockets
/// (with SSE / long-polling fallback).  Using a typed client interface catches
/// method-name and argument mismatches at compile time.
///
/// Java parallel:
///   Spring WebSocket @MessageMapping class + SimpMessagingTemplate
///   STOMP @SendTo("/topic/balances")  →  Clients.All.ReceiveBalanceChanged(...)
/// </summary>
public class BankingHub : Hub<IBankingClient>
{
    /// <summary>
    /// Invoked from a connected client to subscribe to balance events for an account.
    /// Adds the connection to a named SignalR group so only relevant subscribers
    /// receive future notifications.
    ///
    /// Java parallel:
    ///   @SubscribeMapping + session.subscribe(...)
    /// </summary>
    public async Task Subscribe(int accountId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(accountId));

    /// <summary>Unsubscribe from an account's balance-change group.</summary>
    public async Task Unsubscribe(int accountId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(accountId));

    public static string GroupName(int accountId) => $"account-{accountId}";
}

/// <summary>
/// Typed client interface — defines the methods the server can call on clients.
/// Java parallel: STOMP message destination / SimpMessagingTemplate.convertAndSend(...)
/// </summary>
public interface IBankingClient
{
    /// <summary>Server pushes a balance-changed event to subscribed clients.</summary>
    Task ReceiveBalanceChanged(BalanceChangedEvent evt);
}

public record BalanceChangedEvent(int AccountId, decimal NewBalance, string Reason);
