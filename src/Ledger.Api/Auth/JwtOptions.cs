namespace Ledger.Api.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "ledger-api";
    public string Audience { get; set; } = "ledger-clients";
    /// <summary>HMAC-ключ (dev). В проде — асимметричный ключ от IdP (Keycloak/IdentityServer), API хранит только публичный.</summary>
    public string SigningKey { get; set; } = "";
    public int TokenLifetimeMinutes { get; set; } = 60;
    /// <summary>Эндпоинт выдачи токенов для локальной разработки и тестов. В проде — выключен.</summary>
    public bool DevTokenEndpoint { get; set; } = false;
}
