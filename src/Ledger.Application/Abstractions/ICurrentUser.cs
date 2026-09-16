namespace Ledger.Application.Abstractions;

/// <summary>Кто выполняет запрос. Реализация в Api берёт claims из JWT; в тестах — подставляется.</summary>
public interface ICurrentUser
{
    string Subject { get; }
    bool IsAdmin { get; }
}

/// <summary>Аутентифицирован, но не имеет прав на этот ресурс → HTTP 403.</summary>
public sealed class ForbiddenException(string message) : Exception(message);
