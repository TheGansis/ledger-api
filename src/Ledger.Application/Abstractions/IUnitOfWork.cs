namespace Ledger.Application.Abstractions;

public interface IUnitOfWork
{
    /// <summary>
    /// Выполняет работу в одной транзакции БД. Уровень изоляции — READ COMMITTED (дефолт PG):
    /// корректность обеспечивают явные блокировки строк, а не сериализация.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
