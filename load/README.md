# Нагрузочные сценарии (k6)

Запуск API в Release с поднятым лимитом операций (иначе rate limiter сработает раньше, чем БД):

```bash
dotnet run -c Release --project src/Ledger.Api --no-launch-profile \
  --urls http://localhost:5180 RateLimiting:MoneyOpsPerMinute=100000000
```

```bash
k6 run load/transfers.js            # 100 VU × 60 с, 200 счетов, случайные пары
k6 run load/hot-account.js          # 100 VU × 30 с, все переводы на один счёт
k6 run load/idempotent-replays.js   # 50 VU × 30 с, каждый запрос ×5 с одним ключом
```

Переменные: `BASE_URL`, `VUS`, `DURATION`, `ACCOUNTS`. Каждый сценарий в `teardown` сверяет,
что сумма балансов не изменилась (money conservation).

Результаты последнего прогона — в корневом README, раздел «Нагрузка».
