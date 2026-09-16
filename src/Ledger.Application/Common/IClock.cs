namespace Ledger.Application.Common;

/// <summary>Абстракция времени — чтобы тесты были детерминированными.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
