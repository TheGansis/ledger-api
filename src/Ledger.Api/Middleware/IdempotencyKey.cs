namespace Ledger.Api.Middleware;

public static class IdempotencyKey
{
    public const string Header = "Idempotency-Key";

    /// <summary>Ключ обязателен для всех денежных операций — без него клиент не сможет безопасно повторить запрос.</summary>
    public static string Require(HttpRequest request)
    {
        var key = request.Headers[Header].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            throw new BadHttpRequestException($"Header '{Header}' is required (1–128 chars).");
        return key;
    }
}
