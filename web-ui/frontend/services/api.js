// ==================== API 服务层 ====================
const API_BASE = 'http://localhost:8888';

async function apiGet(path) {
  const r = await fetch(API_BASE + path);
  if (!r.ok) throw new Error('HTTP ' + r.status);
  return r.json();
}

async function apiPost(path, body = {}) {
  const r = await fetch(API_BASE + path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  });
  if (!r.ok) throw new Error('HTTP ' + r.status);
  return r.json();
}
