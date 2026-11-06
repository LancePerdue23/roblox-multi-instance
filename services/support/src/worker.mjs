const product = 'roblox-multi-instance';
const encoder = new TextEncoder();
const claimLifetime = 30 * 86400;
const base64url = bytes => btoa(String.fromCharCode(...new Uint8Array(bytes))).replace(/=/g, '').replace(/\+/g, '-').replace(/\//g, '_');
const randomId = () => base64url(crypto.getRandomValues(new Uint8Array(24)));
const hex = bytes => [...new Uint8Array(bytes)].map(x => x.toString(16).padStart(2, '0')).join('');
const sha256 = async value => hex(await crypto.subtle.digest('SHA-256', encoder.encode(value)));
const equal = (a, b) => { if (a.length !== b.length) return false; let diff = 0; for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i); return diff === 0; };
const json = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json', 'cache-control': 'no-store', 'x-content-type-options': 'nosniff' } });
const configured = env => env.SUPPORT_DB && env.SUPPORT_RATE && env.STRIPE_SECRET_KEY && env.STRIPE_WEBHOOK_SECRET && env.STRIPE_PRICE_ID && env.RECEIPT_PRIVATE_JWK && /^[A-Za-z0-9_-]{1,64}$/.test(env.RECEIPT_KEY_ID ?? '') && ['true', 'false'].includes(env.STRIPE_LIVE_MODE);
const exactFields = (body, fields) => body && typeof body === 'object' && !Array.isArray(body) && Object.keys(body).length === fields.length && fields.every(key => Object.hasOwn(body, key));

async function boundedBody(request, maximum) {
  if (Number(request.headers.get('content-length')) > maximum) throw new Error('body');
  if (!request.body) return '';
  const reader = request.body.getReader(); let size = 0; const chunks = [];
  try { for (;;) { const { done, value } = await reader.read(); if (done) break; size += value.length; if (size > maximum) { await reader.cancel(); throw new Error('body'); } chunks.push(value); } }
  finally { reader.releaseLock(); }
  const bytes = new Uint8Array(size); let offset = 0; for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
  return new TextDecoder('utf-8', { fatal: true }).decode(bytes);
}

export async function verifyStripeSignature(raw, signature, secret, now = Math.floor(Date.now() / 1000)) {
  if (typeof signature !== 'string' || signature.length > 2048) return false;
  const fields = signature.split(',').map(x => x.split('='));
  const timestamps = fields.filter(([key]) => key === 't');
  if (timestamps.length !== 1 || !/^\d+$/.test(timestamps[0][1])) return false;
  const timestamp = Number(timestamps[0][1]);
  if (!Number.isSafeInteger(timestamp) || Math.abs(now - timestamp) > 300) return false;
  const key = await crypto.subtle.importKey('raw', encoder.encode(secret), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  const expected = hex(await crypto.subtle.sign('HMAC', key, encoder.encode(`${timestamp}.${raw}`)));
  return fields.some(([name, value]) => name === 'v1' && /^[0-9a-f]{64}$/.test(value ?? '') && equal(expected, value));
}

export async function signReceipt(attemptId, env) {
  const jwk = JSON.parse(env.RECEIPT_PRIVATE_JWK);
  if (jwk.kty !== 'EC' || jwk.crv !== 'P-256' || !jwk.d) throw new Error('key');
  const key = await crypto.subtle.importKey('jwk', jwk, { name: 'ECDSA', namedCurve: 'P-256' }, false, ['sign']);
  const payload = encoder.encode(JSON.stringify({ product, badge: 'supporter', receiptId: attemptId, version: 1, keyId: env.RECEIPT_KEY_ID }));
  const signature = await crypto.subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, key, payload);
  return `${base64url(payload)}.${base64url(signature)}`;
}

export function createHandler({ fetch: fetcher = globalThis.fetch, now = () => Math.floor(Date.now() / 1000) } = {}) {
  return async (request, env) => {
    const url = new URL(request.url);
    if (url.pathname === '/support/thanks' && request.method === 'GET') return new Response('Thank you. Return to Roblox Multi Instance and choose Check payment to receive your optional supporter badge. All features remain free.', { headers: { 'content-type': 'text/plain; charset=utf-8', 'cache-control': 'no-store', 'referrer-policy': 'no-referrer' } });
    if (url.pathname === '/support/cancel' && request.method === 'GET') return new Response('Checkout closed without confirming payment. Return to Roblox Multi Instance to resume checkout or check payment status if you already paid. All features remain free.', { headers: { 'content-type': 'text/plain; charset=utf-8', 'cache-control': 'no-store', 'referrer-policy': 'no-referrer' } });
    if (request.method !== 'POST' || !['/support/start', '/support/claim', '/support/webhook'].includes(url.pathname)) return json({ error: 'not_found' }, 404);
    if (!configured(env)) return json({ error: 'unconfigured' }, 503);
    try {
      if (url.pathname === '/support/webhook') {
        const raw = await boundedBody(request, 256 * 1024);
        if (!await verifyStripeSignature(raw, request.headers.get('stripe-signature'), env.STRIPE_WEBHOOK_SECRET, now())) return json({ error: 'signature' }, 400);
        const event = JSON.parse(raw);
        if (event.livemode !== (env.STRIPE_LIVE_MODE === 'true')) return json({ error: 'mode' }, 400);
        if (!['checkout.session.completed', 'checkout.session.async_payment_succeeded'].includes(event.type)) return json({ received: true });
        const session = event.data?.object;
        if (!session || session.object !== 'checkout.session' || session.mode !== 'payment' || session.livemode !== (env.STRIPE_LIVE_MODE === 'true') || session.metadata?.supportProduct !== product) return json({ error: 'session' }, 400);
        if (session.payment_status !== 'paid' || !Number.isSafeInteger(session.amount_total) || session.amount_total <= 0) return json({ received: true });
        if (!/^[A-Za-z0-9_-]{32}$/.test(session.metadata.attemptId ?? '') || typeof session.id !== 'string' || session.id.length > 256) return json({ error: 'session' }, 400);
        // Matching the server-created Checkout Session binds the actual configured product/price.
        // This transition is naturally idempotent for duplicate and reordered notifications.
        await env.SUPPORT_DB.prepare("UPDATE support_attempts SET status='paid' WHERE attempt_id=? AND session_id=? AND expires_at>?").bind(session.metadata.attemptId, session.id, now()).run();
        return json({ received: true });
      }
      // Route-wide approximate limits avoid storing or keying counters on visitor identity/IP.
      if (!(await env.SUPPORT_RATE.limit({ key: url.pathname })).success) return json({ error: 'rate_limit' }, 429);
      let body;
      try { body = JSON.parse(await boundedBody(request, 2048)); } catch { return json({ error: 'request' }, 400); }
      if (url.pathname === '/support/start') {
        if (!exactFields(body, ['claimHash']) || typeof body.claimHash !== 'string' || !/^[0-9a-f]{64}$/.test(body.claimHash)) return json({ error: 'request' }, 400);
        let attempt = await env.SUPPORT_DB.prepare('SELECT * FROM support_attempts WHERE claim_hash=?').bind(body.claimHash).first();
        if (!attempt) {
          const budget = await env.SUPPORT_DB.prepare('INSERT INTO support_daily_budget(day,used) VALUES(?,1) ON CONFLICT(day) DO UPDATE SET used=used+1 WHERE used<100 RETURNING used').bind(Math.floor(now() / 86400)).first();
          if (!budget) return json({ error: 'daily_limit' }, 429);
          await env.SUPPORT_DB.prepare("INSERT OR IGNORE INTO support_attempts(attempt_id,claim_hash,status,created_at,expires_at) VALUES(?,?,'pending',?,?)").bind(randomId(), body.claimHash, now(), now() + claimLifetime).run();
          attempt = await env.SUPPORT_DB.prepare('SELECT * FROM support_attempts WHERE claim_hash=?').bind(body.claimHash).first();
        }
        if (!attempt || attempt.created_at + 86400 <= now()) return json({ error: 'checkout_expired' }, 409);
        const attemptId = attempt.attempt_id;
        if (attempt.session_id && attempt.checkout_url) return json({ attemptId, checkoutUrl: attempt.checkout_url });
        const form = new URLSearchParams({ mode: 'payment', 'line_items[0][price]': env.STRIPE_PRICE_ID, 'line_items[0][quantity]': '1', 'payment_method_types[0]': 'card', 'metadata[supportProduct]': product, 'metadata[attemptId]': attemptId, success_url: `${url.origin}/support/thanks`, cancel_url: `${url.origin}/support/cancel`, expires_at: String(attempt.created_at + 86400) });
        const response = await fetcher('https://api.stripe.com/v1/checkout/sessions', { method: 'POST', headers: { authorization: `Bearer ${env.STRIPE_SECRET_KEY}`, 'content-type': 'application/x-www-form-urlencoded', 'idempotency-key': attemptId }, body: form.toString(), redirect: 'error', signal: AbortSignal.timeout(15000) });
        if (!response.ok) throw new Error('provider');
        const session = JSON.parse(await boundedBody(response, 32768));
        const checkout = new URL(session.url);
        if (checkout.protocol !== 'https:' || checkout.hostname !== 'checkout.stripe.com' || checkout.port || checkout.username || checkout.password || typeof session.id !== 'string' || session.id.length > 256 || session.livemode !== (env.STRIPE_LIVE_MODE === 'true')) throw new Error('provider');
        await env.SUPPORT_DB.prepare('UPDATE support_attempts SET session_id=?,checkout_url=? WHERE attempt_id=?').bind(session.id, checkout.href, attemptId).run();
        return json({ attemptId, checkoutUrl: checkout.href });
      }
      if (!exactFields(body, ['attemptId', 'secret']) || typeof body.attemptId !== 'string' || typeof body.secret !== 'string' || !/^[A-Za-z0-9_-]{32}$/.test(body.attemptId) || !/^[A-Za-z0-9_-]{43}$/.test(body.secret)) return json({ error: 'request' }, 400);
      const attempt = await env.SUPPORT_DB.prepare('SELECT claim_hash,status,expires_at FROM support_attempts WHERE attempt_id=?').bind(body.attemptId).first();
      if (!attempt || !equal(attempt.claim_hash, await sha256(body.secret)) || attempt.expires_at <= now()) return json({ error: 'claim' }, 404);
      if (attempt.status !== 'paid') return json({ status: 'pending' });
      return json({ status: 'paid', receipt: await signReceipt(body.attemptId, env) });
    } catch { return json({ error: 'unavailable' }, 503); }
  };
}

export default {
  fetch: createHandler(),
  async scheduled(_event, env) { if (env.SUPPORT_DB) { const now = Math.floor(Date.now() / 1000); await env.SUPPORT_DB.prepare('DELETE FROM support_attempts WHERE expires_at<?').bind(now).run(); await env.SUPPORT_DB.prepare('DELETE FROM support_daily_budget WHERE day<?').bind(Math.floor(now / 86400) - 2).run(); } }
};
