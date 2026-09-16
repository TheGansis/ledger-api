// Сценарий «много счетов»: 200 счетов одного пользователя, переводы между случайными парами.
// Модель реального дня: контеншн на одном счёте низкий, нагрузка — на БД и pipeline.
import http from 'k6/http';
import { check } from 'k6';
import { Rate, Trend } from 'k6/metrics';
import { BASE, token, auth, openAccount, deposit, key } from './common.js';

const ACCOUNTS = Number(__ENV.ACCOUNTS || 200);
const rejected = new Rate('transfers_rejected');
const created = new Rate('transfers_created');
const latency = new Trend('transfer_latency', true);

export const options = {
  scenarios: {
    transfers: {
      executor: 'constant-vus',
      vus: Number(__ENV.VUS || 100),
      duration: __ENV.DURATION || '60s',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.001'],           // 5xx/сеть — не больше 0.1 %
    transfer_latency: ['p(95)<250', 'p(99)<500'],
    transfers_created: ['rate>0.99'],          // почти всё должно быть 201 (не replay, не отказ по средствам)
  },
};

export function setup() {
  const tok = token('load-user');
  const ids = [];
  for (let i = 0; i < ACCOUNTS; i++) {
    const id = openAccount(tok, `Load ${i}`);
    deposit(tok, id, 1_000_000);
    ids.push(id);
  }
  return { tok, ids, total: ACCOUNTS * 1_000_000 };
}

export default function (data) {
  const from = data.ids[Math.floor(Math.random() * data.ids.length)];
  let to = data.ids[Math.floor(Math.random() * data.ids.length)];
  if (to === from) to = data.ids[(data.ids.indexOf(from) + 1) % data.ids.length];
  const amount = 1 + Math.floor(Math.random() * 100);

  const r = http.post(`${BASE}/api/transactions/transfers`,
    JSON.stringify({ fromAccountId: from, toAccountId: to, amount }),
    auth(data.tok, { 'Idempotency-Key': key() }));

  latency.add(r.timings.duration);
  created.add(r.status === 201 && r.json('status') === 'Completed');
  rejected.add(r.status === 201 && r.json('status') === 'Rejected');
  check(r, { 'status 201': (x) => x.status === 201 });
}

export function teardown(data) {
  // Инвариант: деньги не появились и не исчезли.
  let sum = 0;
  for (const id of data.ids) sum += http.get(`${BASE}/api/accounts/${id}`, auth(data.tok)).json('balance');
  const ok = Math.abs(sum - data.total) < 0.00001;
  console.log(`money conservation: expected ${data.total}, actual ${sum} → ${ok ? 'OK' : 'BROKEN'}`);
  if (!ok) throw new Error('money conservation violated');
}
