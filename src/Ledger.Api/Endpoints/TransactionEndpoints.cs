using FluentValidation;
using Ledger.Application.Transactions;
using Ledger.Api.Middleware;
using Ledger.Api.Auth;

namespace Ledger.Api.Endpoints;

public static class TransactionEndpoints
{
    public static IEndpointRouteBuilder MapTransactions(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/transactions").WithTags("Transactions").RequireAuthorization();

        group.MapPost("/transfers", async (TransferRequest req, HttpRequest http,
            IValidator<TransferRequest> validator, TransactionService svc, CancellationToken ct) =>
        {
            await validator.ValidateAndThrowAsync(req, ct);
            var result = await svc.TransferAsync(IdempotencyKey.Require(http), req, ct);
            return AccountEndpoints.TransactionResponse(result);
        }).WithSummary("Перевод между счетами (Idempotency-Key обязателен)").Produces<TransactionDto>(201).Produces<TransactionDto>(200)
          .RequireRateLimiting(RateLimitPolicies.MoneyOps);

        group.MapGet("/{id:guid}", async (Guid id, TransactionService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } dto ? Results.Ok(dto) : Results.NotFound())
            .WithSummary("Транзакция по id");

        return app;
    }
}
