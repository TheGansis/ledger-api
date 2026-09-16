// Сценарий «ретраи клиентов»: каждый VU шлёт один и тот же запрос 5 раз подряд с одним ключом.
// Проверяем: ровно одно списание, повторы быстрые (быстрый путь без блокировок).
import http from 'k6/http';
import { check } from 'k6';
import { Trend, Counter } from 'k6/metrics';
import { BASE, token, auth, openAccount, deposit, key } from './common.js';

const first = new Trend('first_request_latency', true);
const replay = new Trend('replay_latency', true);
const mismatches = new Counter('replay_id_mismatch');

export const options = {
  scenarios: { replays: { executor: 'constant-vus', vus: Number(__ENV.VUS || 50), duration: __ENV.DURATION || '30s' } },
  thresholds: { http_req_failed: ['rate<0.001'], replay_id_mismatch: ['count==0'], replay_latency: ['p(95)<50'] },
};

export function setup() {
  const tok = token('replay-user');
  const a = openAccount(tok, 'A'); deposit(tok, a, 100_000_000);
  const b = openAccount(tok, 'B');
  return { tok, a, b };
}

export default function (data) {
  const k = key();
  const body = JSON.stringify({ fromAccountId: data.a, toAccountId: data.b, amount: 1 });
  const r1 = http.post(`${BASE}/api/transactions/transfers`, body, auth(data.tok, { 'Idempotency-Key': k }));
  first.add(r1.timings.duration);
  check(r1, { 'first is 201': (x) => x.status === 201 });
  const id = r1.json('id');
  for (let i = 0; i < 4; i++) {
    const r = http.post(`${BASE}/api/transactions/transfers`, body, auth(data.tok, { 'Idempotency-Key': k }));
    replay.add(r.timings.duration);
    check(r, { 'replay is 200': (x) => x.status === 200 });
    if (r.json('id') !== id) mismatches.add(1);
  }
}
