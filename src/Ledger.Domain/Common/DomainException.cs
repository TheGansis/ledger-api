namespace Ledger.Domain.Common;

/// <summary>Нарушение бизнес-правила. Транслируется в HTTP 422 на границе API.</summary>
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
