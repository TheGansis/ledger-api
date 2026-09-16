namespace Ledger.Application.Abstractions;

/// <summary>
/// Transactional outbox: событие пишется в ту же транзакцию БД, что и бизнес-данные.
/// Отдельный публикатор доставляет его в брокер. Так исключён сценарий
/// «в базе сохранили, брокер упал — событие потеряно» (и наоборот).
/// </summary>
public interface IOutbox
{
    void Enqueue<T>(string route, T payload) where T : notnull;
}
