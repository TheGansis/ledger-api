// Сценарий «горячий счёт»: все VU переводят на ОДИН счёт с 50 разных.
// Худший случай для блокировок: каждая транзакция ждёт FOR UPDATE на получателе.
// Показывает, что латентность растёт линейно от контеншна, но ошибок и дедлоков нет.
import http from 'k6/http';
import { check } from 'k6';
import { Rate, Trend } from 'k6/metrics';
import { BASE, token, auth, openAccount, deposit, key } from './common.js';

const SENDERS = 50;
const latency = new Trend('transfer_latency', true);
const created = new Rate('transfers_created');

export const options = {
  scenarios: {
    hot: { executor: 'constant-vus', vus: Number(__ENV.VUS || 100), duration: __ENV.DURATION || '30s' },
  },
  thresholds: {
    http_req_failed: ['rate<0.001'],
    transfers_created: ['rate>0.99'],
  },
};

export function setup() {
  const tok = token('hot-user');
  const hot = openAccount(tok, 'Hot');
  const senders = [];
  for (let i = 0; i < SENDERS; i++) {
    const id = openAccount(tok, `Sender ${i}`);
    deposit(tok, id, 10_000_000);
    senders.push(id);
  }
  return { tok, hot, senders, total: SENDERS * 10_000_000 };
}

export default function (data) {
  const from = data.senders[__VU % data.senders.length];
  const r = http.post(`${BASE}/api/transactions/transfers`,
    JSON.stringify({ fromAccountId: from, toAccountId: data.hot, amount: 1 }),
    auth(data.tok, { 'Idempotency-Key': key() }));
  latency.add(r.timings.duration);
  created.add(r.status === 201 && r.json('status') === 'Completed');
  check(r, { 'status 201': (x) => x.status === 201 });
}

export function teardown(data) {
  let sum = http.get(`${BASE}/api/accounts/${data.hot}`, auth(data.tok)).json('balance');
  for (const id of data.senders) sum += http.get(`${BASE}/api/accounts/${id}`, auth(data.tok)).json('balance');
  const ok = Math.abs(sum - data.total) < 0.00001;
  console.log(`money conservation: expected ${data.total}, actual ${sum} → ${ok ? 'OK' : 'BROKEN'}`);
  if (!ok) throw new Error('money conservation violated');
}
