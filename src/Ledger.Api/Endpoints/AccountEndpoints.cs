using FluentValidation;
using Ledger.Application.Accounts;
using Ledger.Application.Transactions;
using Ledger.Api.Middleware;
using Ledger.Domain.Accounts;
using Ledger.Api.Auth;

namespace Ledger.Api.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccounts(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts").WithTags("Accounts").RequireAuthorization();

        group.MapPost("/", async (OpenAccountRequest req, IValidator<OpenAccountRequest> validator, AccountService svc, CancellationToken ct) =>
        {
            await validator.ValidateAndThrowAsync(req, ct);
            var dto = await svc.OpenAsync(req, ct);
            return Results.Created($"/api/accounts/{dto.Id}", dto);
        }).WithSummary("Открыть счёт").Produces<AccountDto>(201).ProducesValidationProblem();

        group.MapGet("/{id:guid}", async (Guid id, AccountService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } dto ? Results.Ok(dto) : Results.NotFound())
            .WithSummary("Счёт по id").Produces<AccountDto>().Produces(404);

        group.MapGet("/{id:guid}/entries", async (Guid id, string? cursor, int? limit, AccountService svc, CancellationToken ct) =>
            await svc.GetStatementAsync(id, cursor, limit ?? 50, ct) is { } page ? Results.Ok(page) : Results.NotFound())
            .WithSummary("Выписка (курсорная пагинация, от новых к старым)");

        group.MapPost("/{id:guid}/deposit", async (Guid id, MoneyOperationRequest req, HttpRequest http,
            IValidator<MoneyOperationRequest> validator, TransactionService svc, CancellationToken ct) =>
        {
            await validator.ValidateAndThrowAsync(req, ct);
            var result = await svc.DepositAsync(IdempotencyKey.Require(http), id, req, ct);
            return TransactionResponse(result);
        }).WithSummary("Пополнить счёт (Idempotency-Key обязателен)").RequireRateLimiting(RateLimitPolicies.MoneyOps);

        group.MapPost("/{id:guid}/withdraw", async (Guid id, MoneyOperationRequest req, HttpRequest http,
            IValidator<MoneyOperationRequest> validator, TransactionService svc, CancellationToken ct) =>
        {
            await validator.ValidateAndThrowAsync(req, ct);
            var result = await svc.WithdrawAsync(IdempotencyKey.Require(http), id, req, ct);
            return TransactionResponse(result);
        }).WithSummary("Снять со счёта (Idempotency-Key обязателен)").RequireRateLimiting(RateLimitPolicies.MoneyOps);

        group.MapPost("/{id:guid}/freeze", async (Guid id, AccountService svc, CancellationToken ct) =>
            await svc.SetStatusAsync(id, AccountStatus.Frozen, ct) is { } dto ? Results.Ok(dto) : Results.NotFound())
            .WithSummary("Заморозить счёт (admin)").RequireAuthorization(Policies.Admin);

        group.MapPost("/{id:guid}/unfreeze", async (Guid id, AccountService svc, CancellationToken ct) =>
            await svc.SetStatusAsync(id, AccountStatus.Active, ct) is { } dto ? Results.Ok(dto) : Results.NotFound())
            .WithSummary("Разморозить счёт (admin)").RequireAuthorization(Policies.Admin);

        group.MapPost("/{id:guid}/close", async (Guid id, AccountService svc, CancellationToken ct) =>
            await svc.SetStatusAsync(id, AccountStatus.Closed, ct) is { } dto ? Results.Ok(dto) : Results.NotFound())
            .WithSummary("Закрыть счёт (admin, только с нулевым балансом)").RequireAuthorization(Policies.Admin);

        return app;
    }

    /// <summary>
    /// 201 — транзакция создана этим запросом; 200 — повтор, вернули существующую.
    /// Отклонённая транзакция — это тоже 201/200 с Status=Rejected: запрос обработан, результат — отказ.
    /// </summary>
    public static IResult TransactionResponse(TransactionResult result) =>
        result.Replayed
            ? Results.Ok(result.Transaction)
            : Results.Created($"/api/transactions/{result.Transaction.Id}", result.Transaction);
}
