using System.Security.Claims;
using Ledger.Application.Abstractions;

namespace Ledger.Api.Auth;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public string Subject =>
        Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? Principal?.FindFirstValue("sub")
        ?? throw new UnauthorizedAccessException("No authenticated subject.");

    public bool IsAdmin => Principal?.IsInRole(Roles.Admin) ?? false;
}

public static class Roles
{
    public const string Admin = "admin";
    public const string Customer = "customer";
}
