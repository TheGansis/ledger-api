# Ledger API

[![CI](https://github.com/TheGansis/ledger-api/actions/workflows/ci.yml/badge.svg)](https://github.com/TheGansis/ledger-api/actions/workflows/ci.yml)

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
- **JWT-авторизация**: счёт принадлежит `sub` из токена; чужой счёт — `403`; заморозка/закрытие — роль `admin`;
  dev-эндпоинт выдачи токенов для локальной работы (в проде — внешний IdP)
- **Rate limiting** денежных операций на пользователя (fixed window, `429` + `Retry-After`)
- **Наблюдаемость**: Serilog (JSON в проде) с `traceId`/`UserId`/`IdempotencyKey` в каждой записи;
  OpenTelemetry-трейсы (HTTP, Npgsql, собственные span'ы операций) → Jaeger по OTLP;
  Prometheus `/metrics` с бизнес-метриками: `ledger_transactions_total{type,status,reason}`,
  `ledger_operation_duration`, `ledger_idempotency_replays`, `ledger_outbox_pending/lag`
- Ошибки в формате RFC 7807 ProblemDetails с машиночитаемым полем `code`
- Health `/health` с деталями: postgres, redis, **отставание outbox**; Swagger UI на `/swagger`
- **CI (GitHub Actions)**: сборка, все тесты против PostgreSQL/Redis/RabbitMQ в service-контейнерах, сборка Docker-образов

## Быстрый старт

```bash
docker compose up --build
# API        http://localhost:8080/swagger
# RabbitMQ   http://localhost:15672  (guest/guest)
# Jaeger     http://localhost:16686  (трейсы запросов и SQL)
# Prometheus http://localhost:9090   (ledger_* метрики)
```

Без Docker (нужны PostgreSQL 16 с пользователем/базой `ledger`/`ledger`, Redis и RabbitMQ на localhost):

```bash
dotnet run --project src/Ledger.Api             # миграции применяются при старте
dotnet run --project src/Ledger.Notifications   # консьюмер уведомлений (пишет в лог)
```

## Пример

```bash
J='Content-Type: application/json'
# токен клиента (dev-эндпоинт; роли: customer по умолчанию, admin)
T=$(curl -s -X POST localhost:8080/api/auth/dev-token -H "$J" -d '{"subject":"alice"}' | jq -r .accessToken)
A="Authorization: Bearer $T"

ACC=$(curl -s -X POST localhost:8080/api/accounts -H "$J" -H "$A" -d '{"ownerName":"Alice","currency":"RUB"}' | jq -r .id)
curl -X POST localhost:8080/api/accounts/$ACC/deposit -H "$J" -H "$A" -H 'Idempotency-Key: dep-1' -d '{"amount":1000}'

# перевод на счёт другого пользователя — со своего счёта можно, с чужого — 403
curl -X POST localhost:8080/api/transactions/transfers -H "$J" -H "$A" -H 'Idempotency-Key: tr-1' \
     -d "{\"fromAccountId\":\"$ACC\",\"toAccountId\":\"<id счёта Bob>\",\"amount\":300}"

curl -H "$A" "localhost:8080/api/accounts/$ACC/entries?limit=20"
curl localhost:8080/metrics | grep ledger_
```

## API

| Метод | Путь | Описание |
|---|---|---|
| POST | `/api/auth/dev-token` | DEV: `{subject, roles?}` → JWT |
| POST | `/api/accounts` | Открыть счёт `{ownerName, currency, ownerId?}` (`ownerId` — только admin) |
| GET | `/api/accounts/{id}` | Счёт |
| GET | `/api/accounts/{id}/entries?limit&cursor` | Выписка, от новых к старым |
| POST | `/api/accounts/{id}/deposit` | Пополнение `{amount}` + `Idempotency-Key` |
| POST | `/api/accounts/{id}/withdraw` | Снятие `{amount}` + `Idempotency-Key` |
| POST | `/api/accounts/{id}/freeze` · `/unfreeze` · `/close` | Смена статуса (admin) |
| POST | `/api/transactions/transfers` | Перевод `{fromAccountId, toAccountId, amount}` + `Idempotency-Key` |
| GET | `/api/transactions/{id}` | Транзакция |

Коды ответов денежных операций: `201` — создана; `200` — повтор по ключу; `400` — валидация /
нет ключа; `401` — нет/невалидный токен; `403` — чужой счёт или нет роли; `404` — счёт не найден;
`409` — ключ занят другим запросом; `422` — нарушение бизнес-правила вне транзакции; `429` — лимит операций.

## Архитектура

```
src/
  Ledger.Domain          сущности и правила: Account, Transaction, LedgerEntry, Money — без зависимостей
  Ledger.Application     use-case'ы (AccountService, TransactionService), валидаторы, интерфейсы репозиториев
  Ledger.Infrastructure  EF Core, конфигурации таблиц, миграции, репозитории, UnitOfWork
  Ledger.Api             minimal API, JWT, rate limiting, ProblemDetails, Swagger, health, Serilog, OpenTelemetry
  Ledger.Notifications   worker: консьюмер RabbitMQ с дедупликацией в Redis
tests/
  Ledger.Domain.Tests    unit-тесты правил (17)
  Ledger.Api.Tests       интеграционные тесты через WebApplicationFactory поверх настоящих PostgreSQL,
                         RabbitMQ и Redis (38): 100 встречных параллельных переводов, гонка одинаковых
                         ключей, доставка события в брокер, инвалидация кэша, авторизация, rate limit, метрики
```

## Тесты

```bash
dotnet test   # нужны база ledger_test (или LEDGER_TEST_DB), Redis и RabbitMQ на localhost
```

## Нагрузка

Сценарии k6 в [`load/`](load/) (все проверяют в `teardown`, что сумма балансов не изменилась).
Стенд: Apple M5 Pro, PostgreSQL 16 / Redis / RabbitMQ локально (brew), API в Release, k6 на той же машине.

| Сценарий | Нагрузка | Пропускная способность | p50 / p95 / p99 | Ошибок | Деньги сошлись |
|---|---|---|---|---|---|
| `transfers.js` — 200 счетов, случайные пары | 100 VU × 60 с | **2 254 переводов/с** (136 000 за минуту) | 36 / 107 / ~200 мс | 0 | ✅ |
| `hot-account.js` — все переводы на один счёт | 100 VU × 30 с | 352 переводов/с | 209 / 719 / ~1 300 мс | 0 | ✅ |
| `idempotent-replays.js` — каждый запрос ×5 с одним ключом | 50 VU × 30 с | 1 677 req/с | повтор: **0.39 / 0.51 мс** | 0, 0 несовпадений id | — |

Каждый перевод — это транзакция БД с двумя `FOR UPDATE`, тремя INSERT (транзакция, две проводки),
двумя UPDATE балансов и INSERT в outbox. Публикатор outbox выгребает ~3 300 сообщений/с — быстрее
входящего потока, отставание после пика рассасывается за секунды, и это видно в `/health` (`Degraded` при лаге > 10 с).

Что показала нагрузка:
- **Горячий счёт** — ожидаемое узкое место: все транзакции выстраиваются в очередь за одной строкой,
  латентность растёт линейно от числа конкурентов, но **ни одного дедлока и ни одной потерянной копейки**.
  Лечится на уровне продукта (лимит операций на счёт, шардирование «кошельков» крупных получателей),
  не на уровне блокировок.
- **Пул соединений должен быть меньше `max_connections` PostgreSQL.** Первый прогон горячего счёта дал
  0.12 % ошибок `53300: remaining connection slots are reserved` — 100 VU + публикатор + health превысили
  лимит 100 у PostgreSQL. После `Maximum Pool Size=80` ошибки исчезли, а пропускная способность горячего
  счёта **выросла** с 262 до 352/с (меньше соединений — меньше борьбы за одну строку). В проде — PgBouncer.
- **Повтор по ключу идемпотентности — 0.4 мс:** быстрый путь не берёт блокировок и не открывает транзакцию.

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
- [x] JWT-авторизация и владение счетами, rate limiting, Serilog, OpenTelemetry (Jaeger/Prometheus), GitHub Actions CI
- [x] Нагрузочные сценарии k6 и отчёт (2 254 переводов/с, p95 107 мс, 0 ошибок)
