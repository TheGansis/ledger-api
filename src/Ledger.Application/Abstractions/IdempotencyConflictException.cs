namespace Ledger.Application.Abstractions;

/// <summary>
/// Тот же Idempotency-Key, но другое тело запроса. Это ошибка клиента (HTTP 409/422),
/// а не повтор: молча вернуть старый результат было бы опасно.
/// </summary>
public sealed class IdempotencyConflictException(string key)
    : Exception($"Idempotency key '{key}' was already used with a different request.")
{
    public string Key { get; } = key;
}
