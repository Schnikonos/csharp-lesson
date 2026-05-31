using Grpc.Core;
using Lesson.Data;
using Lesson.Grpc;
using Microsoft.EntityFrameworkCore;

namespace Lesson.Services;

/// <summary>
/// Lesson 20-A — gRPC unary service.
///
/// gRPC uses HTTP/2 + Protocol Buffers; the server generates C# base classes
/// from the .proto file at build time (via Grpc.Tools / Grpc.AspNetCore).
/// This service implements the two unary RPCs defined in banking.proto.
///
/// Java parallel:
///   @GrpcService on a class extending BankingServiceGrpc.BankingServiceImplBase
///   StreamObserver&lt;AccountReply&gt; onNext/onCompleted → Grpc.Core async return
/// </summary>
public class GrpcBankingService(BankingDbContext db) : BankingService.BankingServiceBase
{
    // Unary RPC: client sends one request, server returns one response
    public override async Task<AccountReply> GetAccount(
        GetAccountRequest request, ServerCallContext context)
    {
        var entity = await db.BankAccounts.FindAsync([request.Id], context.CancellationToken);
        if (entity is null)
            throw new RpcException(new Status(StatusCode.NotFound, $"Account {request.Id} not found"));

        return Map(entity);
    }

    // Unary RPC: create and return a new account
    public override async Task<AccountReply> CreateAccount(
        CreateAccountRequest request, ServerCallContext context)
    {
        var account = new Lesson.Entities.BankAccount
        {
            AccountNumber = request.AccountNumber,
            OwnerName     = request.OwnerName,
            Balance       = (decimal)request.InitialBalance,
            AccountType   = "Savings",
        };
        db.BankAccounts.Add(account);
        await db.SaveChangesAsync(context.CancellationToken);
        return Map(account);
    }

    // Server-streaming RPC (Lesson 20-B) — also defined here so the proto builds cleanly
    public override async Task ListAccounts(
        ListAccountsRequest request,
        IServerStreamWriter<AccountReply> responseStream,
        ServerCallContext context)
    {
        var accounts = await db.BankAccounts.ToListAsync(context.CancellationToken);
        foreach (var a in accounts)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            await responseStream.WriteAsync(Map(a), context.CancellationToken);
        }
    }

    private static AccountReply Map(Lesson.Entities.BankAccount a) => new()
    {
        Id            = a.Id,
        AccountNumber = a.AccountNumber,
        OwnerName     = a.OwnerName,
        Balance       = (double)a.Balance,
    };
}
