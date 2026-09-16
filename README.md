# Ledger API

Сервис счетов и переводов с двойной записью: ASP.NET Core 10, EF Core, PostgreSQL 16, RabbitMQ, Redis.

Задача проекта — показать, как в .NET-бэкенде решаются проблемы, с которыми сталкивается любая
финансовая система: **идемпотентность** запросов, **конкурентные** операции над одним счётом без
потери денег, **атомарность** перевода, **выписка** без деградации на больших объёмах.

## Возможности

- Счета: открытие, заморозка/разморозка, закрытие (только с нулевым балансом)
- Пополнение, снятие, перевод между счетами
- **Двойная запись**: перевод порождает две проводки (дебет/кредит), сумма проводок по транзакции = 0
- **Идемпотентность**: заголовок `Idempotency-Key` обязателен для всех денежных операций;
  повтор возвращает ту же транзакцию (`200`), повтор с другим телом — `409`
- **Бизнес-отказ ≠ ошибка**: недостаток средств или замороженный счёт фиксируются как
  транзакция со статусом `Rejected` и кодом причины — повтор с тем же ключом вернёт тот же отказ
- **Конкурентность**: `SELECT … FOR UPDATE` с упорядоченными блокировками (без дедлоков),
  `xmin` как concurrency-token, `CHECK (balance >= 0)` в БД как последняя линия защиты
- **Курсорная пагинация** выписки по монотонному `sequence` (identity)
- **Transactional outbox → RabbitMQ**: событие транзакции пишется в той же транзакции БД, что и
  проводки; фоновый публикатор (`FOR UPDATE SKIP LOCKED`, publisher confirms, persistent) доставляет
  его в topic-exchange `ledger.events` с маршрутами `transaction.completed` / `transaction.rejected`
- **Идемпотентный консьюмер** `Ledger.Notifications`: дедупликация по `MessageId` через Redis `SET NX`,
  ручной ack/nack, prefetch
- **Redis cache-aside** для карточки счёта (TTL 30 с, инвалидация после коммита, деградация в БД при недоступности Redis)
- Ошибки в формате RFC 7807 ProblemDetails с машиночитаемым полем `code`
- Health `/health` с деталями: postgres, redis, **отставание outbox**; Swagger UI на `/swagger`

## Быстрый старт

```bash
docker compose up --build        # API http://localhost:8080/swagger, RabbitMQ UI http://localhost:15672
```

Без Docker (нужны PostgreSQL 16 с пользователем/базой `ledger`/`ledger`, Redis и RabbitMQ на localhost):

```bash
dotnet run --project src/Ledger.Api             # миграции применяются при старте
dotnet run --project src/Ledger.Notifications   # консьюмер уведомлений (пишет в лог)
```

## Пример

```bash
A=$(curl -s -X POST localhost:8080/api/accounts -H 'Content-Type: application/json' \
   -d '{"ownerName":"Alice","currency":"RUB"}' | jq -r .id)
B=$(curl -s -X POST localhost:8080/api/accounts -H 'Content-Type: application/json' \
   -d '{"ownerName":"Bob","currency":"RUB"}' | jq -r .id)

curl -X POST localhost:8080/api/accounts/$A/deposit -H 'Content-Type: application/json' \
     -H 'Idempotency-Key: dep-1' -d '{"amount":1000}'

curl -X POST localhost:8080/api/transactions/transfers -H 'Content-Type: application/json' \
     -H 'Idempotency-Key: tr-1' -d "{\"fromAccountId\":\"$A\",\"toAccountId\":\"$B\",\"amount\":300}"

curl "localhost:8080/api/accounts/$A/entries?limit=20"
```

## API

| Метод | Путь | Описание |
|---|---|---|
| POST | `/api/accounts` | Открыть счёт `{ownerName, currency}` |
| GET | `/api/accounts/{id}` | Счёт |
| GET | `/api/accounts/{id}/entries?limit&cursor` | Выписка, от новых к старым |
| POST | `/api/accounts/{id}/deposit` | Пополнение `{amount}` + `Idempotency-Key` |
| POST | `/api/accounts/{id}/withdraw` | Снятие `{amount}` + `Idempotency-Key` |
| POST | `/api/accounts/{id}/freeze` · `/unfreeze` · `/close` | Смена статуса |
| POST | `/api/transactions/transfers` | Перевод `{fromAccountId, toAccountId, amount}` + `Idempotency-Key` |
| GET | `/api/transactions/{id}` | Транзакция |

Коды ответов денежных операций: `201` — создана; `200` — повтор по ключу; `400` — валидация /
нет ключа; `404` — счёт не найден; `409` — ключ занят другим запросом; `422` — нарушение
бизнес-правила вне транзакции (например, закрытие счёта с остатком).

## Архитектура

```
src/
  Ledger.Domain          сущности и правила: Account, Transaction, LedgerEntry, Money — без зависимостей
  Ledger.Application     use-case'ы (AccountService, TransactionService), валидаторы, интерфейсы репозиториев
  Ledger.Infrastructure  EF Core, конфигурации таблиц, миграции, репозитории, UnitOfWork
  Ledger.Api             minimal API, ProblemDetails, Swagger, health
  Ledger.Notifications   worker: консьюмер RabbitMQ с дедупликацией в Redis
tests/
  Ledger.Domain.Tests    unit-тесты правил (17)
  Ledger.Api.Tests       интеграционные тесты через WebApplicationFactory поверх настоящих PostgreSQL,
                         RabbitMQ и Redis (27): 100 встречных параллельных переводов, гонка одинаковых
                         ключей, доставка события в брокер, инвалидация кэша
```

## Тесты

```bash
dotnet test   # нужны база ledger_test (или LEDGER_TEST_DB), Redis и RabbitMQ на localhost
```

## Как устроен перевод

1. Быстрый путь: если `Idempotency-Key` уже есть в `transactions` — вернуть сохранённый результат.
2. Открыть транзакцию БД, взять оба счёта `SELECT … FOR UPDATE ORDER BY id` —
   одинаковый порядок блокировок во всех запросах исключает взаимную блокировку.
3. Повторно проверить ключ уже под блокировкой (гонка двух одинаковых запросов).
4. Проверить правила (валюта, статус, остаток) **до** изменения балансов; отказ сохраняется
   как `Rejected`-транзакция.
5. Записать транзакцию, две проводки, новые балансы **и outbox-сообщение** одним `SaveChanges` и закоммитить.
6. После коммита инвалидировать кэш обоих счетов. Публикатор в фоне заберёт сообщение из outbox
   и отправит в RabbitMQ; консьюмер уведомлений обработает его ровно один раз.

Подробный разбор решений и вопросы для собеседования — в [РАЗБОР.md](РАЗБОР.md).

## Дорожная карта

- [x] Ядро: счета, переводы, идемпотентность, блокировки, выписка, тесты
- [x] Outbox → RabbitMQ, идемпотентный консьюмер уведомлений, Redis cache-aside
- [ ] JWT-авторизация, Serilog, OpenTelemetry, GitHub Actions CI
- [ ] Нагрузочный сценарий k6 и отчёт
