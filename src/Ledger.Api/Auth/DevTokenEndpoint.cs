using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Ledger.Api.Auth;

public sealed record DevTokenRequest(string Subject, string[]? Roles = null);
public sealed record DevTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Выдача JWT для разработки: подписывает тем же симметричным ключом, которым API валидирует.
/// Имитирует IdP. Включается флагом Jwt:DevTokenEndpoint; в проде токены выдаёт внешний провайдер.
/// </summary>
public static class DevTokenEndpoint
{
    public static IEndpointRouteBuilder MapDevTokens(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/dev-token", (DevTokenRequest req, IOptions<JwtOptions> options) =>
        {
            var opt = options.Value;
            if (string.IsNullOrWhiteSpace(req.Subject)) return Results.BadRequest("subject is required");

            var now = DateTimeOffset.UtcNow;
            var expires = now.AddMinutes(opt.TokenLifetimeMinutes);
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, req.Subject),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            };
            claims.AddRange((req.Roles ?? [Roles.Customer]).Select(r => new Claim(ClaimTypes.Role, r)));

            var token = new JwtSecurityToken(opt.Issuer, opt.Audience, claims, now.UtcDateTime, expires.UtcDateTime,
                new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opt.SigningKey)), SecurityAlgorithms.HmacSha256));
            return Results.Ok(new DevTokenResponse(new JwtSecurityTokenHandler().WriteToken(token), expires));
        }).AllowAnonymous().WithTags("Auth").WithSummary("DEV: выдать JWT для subject с ролями (customer по умолчанию, admin)");
        return app;
    }
}
