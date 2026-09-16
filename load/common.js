import http from 'k6/http';
import { check } from 'k6';

export const BASE = __ENV.BASE_URL || 'http://localhost:5180';
const JSON_H = { 'Content-Type': 'application/json' };

export function token(subject, roles) {
  const r = http.post(`${BASE}/api/auth/dev-token`, JSON.stringify({ subject, roles }), { headers: JSON_H });
  check(r, { 'token issued': (x) => x.status === 200 });
  return r.json('accessToken');
}

export function auth(tok, extra = {}) {
  return { headers: Object.assign({}, JSON_H, { Authorization: `Bearer ${tok}` }, extra) };
}

export function openAccount(tok, owner) {
  const r = http.post(`${BASE}/api/accounts`, JSON.stringify({ ownerName: owner, currency: 'RUB' }), auth(tok));
  check(r, { 'account opened': (x) => x.status === 201 });
  return r.json('id');
}

export function deposit(tok, id, amount) {
  const r = http.post(`${BASE}/api/accounts/${id}/deposit`, JSON.stringify({ amount }),
    auth(tok, { 'Idempotency-Key': `seed-${id}-${amount}` }));
  check(r, { 'deposit ok': (x) => x.status === 201 });
}

export function key() {
  return `k6-${__VU}-${__ITER}-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}
