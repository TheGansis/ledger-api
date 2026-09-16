using FluentValidation;
using Ledger.Application.Abstractions;
using Ledger.Application.Transactions;
using Ledger.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Ledger.Api.Middleware;

/// <summary>
/// Единая трансляция исключений в RFC 7807 ProblemDetails. Клиент всегда получает
/// машиночитаемый код ошибки, а не текст из стека.
/// </summary>
public sealed class ProblemDetailsMapping(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, title, code, errors) = ex switch
        {
            ValidationException v => (StatusCodes.Status400BadRequest, "Validation failed", "validation",
                v.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),
            DomainException d => (StatusCodes.Status422UnprocessableEntity, d.Message, d.Code, null),
            AccountNotFoundException => (StatusCodes.Status404NotFound, ex.Message, "account.not_found", null),
            IdempotencyConflictException => (StatusCodes.Status409Conflict, ex.Message, "idempotency.conflict", null),
            BadHttpRequestException b => (b.StatusCode, b.Message, "bad_request", null),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "internal", null),
        };

        http.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = title, Instance = http.Request.Path };
        problem.Extensions["code"] = code;
        if (errors is not null) problem.Extensions["errors"] = errors;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http, ProblemDetails = problem, Exception = ex,
        });
    }
}
