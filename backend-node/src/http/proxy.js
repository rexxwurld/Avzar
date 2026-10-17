import { Readable } from 'node:stream';

const MAX_SEGMENTS = 12;
const BLOCKED_PREFIXES = [['api', 'webhooks'], ['api', 'billing', 'subscription', 'webhook']];
const REQUEST_HEADERS = ['accept', 'accept-language', 'content-type', 'cookie', 'idempotency-key', 'x-xsrf-token', 'user-agent'];
const RESPONSE_HEADERS = ['content-type', 'content-disposition', 'retry-after', 'content-language'];

export function requestBodyTooLarge(contentLength, maxBytes) {
  if (contentLength == null || contentLength === '') return false;
  if (!/^\d+$/.test(String(contentLength))) return true;
  return Number(contentLength) > maxBytes;
}

function boundedBody(req, maxBytes) {
  return Readable.from((async function* () {
    let received = 0;
    for await (const chunk of req) {
      received += chunk.length;
      if (received > maxBytes) {
        req.__avzarBodyTooLarge = true;
        const error = new Error('request_body_too_large');
        error.code = 'REQUEST_BODY_TOO_LARGE';
        throw error;
      }
      yield chunk;
    }
  })());
}

export function safeApiPath(pathname) {
  if (typeof pathname !== 'string' || !pathname.startsWith('/backend/')) return null;
  const relative = pathname.slice('/backend/'.length);
  const segments = relative.split('/');
  if (!segments.length || segments.length > MAX_SEGMENTS || segments[0] !== 'api') return null;
  if (segments.some((segment) => !segment || segment === '.' || segment === '..' || /[%\\?#\u0000-\u001f]/.test(segment))) return null;
  const lower = segments.map((segment) => segment.toLowerCase());
  if (BLOCKED_PREFIXES.some((prefix) => prefix.every((part, index) => lower[index] === part))) return null;
  return segments.map(encodeURIComponent).join('/');
}

export function isSameOrigin(origin, host) {
  if (!origin) return true; // The upstream antiforgery token remains mandatory for cookie-authenticated writes.
  if (!host) return false;
  try { return new URL(origin).host.toLowerCase() === host.toLowerCase(); }
  catch { return false; }
}

function requestHeaders(incoming, trustProxyHeaders) {
  const outgoing = new Headers();
  for (const name of REQUEST_HEADERS) {
    const value = typeof incoming.get === 'function' ? incoming.get(name) : incoming[name];
    if (value) outgoing.set(name, value);
  }
  // Only forward an address when deployment explicitly trusts its ingress proxy to sanitize/append X-Forwarded-For.
  if (trustProxyHeaders) {
    const chain = typeof incoming.get === 'function' ? incoming.get('x-forwarded-for') : incoming['x-forwarded-for'];
    const last = chain?.split(',').map((part) => part.trim()).filter(Boolean).at(-1);
    if (last && /^[0-9a-fA-F:.]{3,45}$/.test(last)) outgoing.set('x-forwarded-for', last);
  }
  return outgoing;
}

function copyResponseHeaders(incoming, outgoing) {
  for (const name of RESPONSE_HEADERS) {
    const value = typeof incoming.get === 'function' ? incoming.get(name) : incoming[name];
    if (value) outgoing.setHeader(name, value);
  }
  for (const value of incoming.getSetCookie()) outgoing.appendHeader('set-cookie', value);
  outgoing.setHeader('cache-control', 'no-store');
  outgoing.setHeader('x-content-type-options', 'nosniff');
}

export async function proxyApiRequest(req, res, apiUrl, trustProxyHeaders = false, maxBodyBytes = 25 * 1024 * 1024, upstreamTimeoutMs = 30000, fetchImpl = fetch) {
  const safePath = safeApiPath(req.originalUrl.split('?')[0]);
  if (!safePath) {
    res.status(404).json({ code: 'route_not_found', message: 'This API route is not available through the browser proxy.' });
    return;
  }
  const unsafe = !['GET', 'HEAD', 'OPTIONS'].includes(req.method);
  if (unsafe && requestBodyTooLarge(req.headers['content-length'], maxBodyBytes)) {
    res.status(413).json({ code: 'request_body_too_large', message: 'The request body exceeds the allowed size.' });
    return;
  }
  if (unsafe && !isSameOrigin(req.get('origin') || null, req.get('host') || null)) {
    res.status(403).json({ code: 'cross_origin_request_refused', message: 'Cross-origin request refused.' });
    return;
  }
  if (!apiUrl) {
    res.status(503).json({ code: 'upstream_not_configured', message: 'The existing Avzar API has not been configured for this PWA deployment.' });
    return;
  }

  let target;
  try {
    target = new URL(`/${safePath}${new URL(req.originalUrl, 'http://localhost').search}`, `${apiUrl.replace(/\/+$/, '')}/`);
  } catch {
    res.status(503).json({ code: 'upstream_not_configured', message: 'The API URL is invalid.' });
    return;
  }

  try {
    const options = { method: req.method, headers: requestHeaders(req.headers, trustProxyHeaders), redirect: 'manual', cache: 'no-store', signal: AbortSignal.timeout(upstreamTimeoutMs) };
    if (unsafe && req.method !== 'OPTIONS') {
      options.body = boundedBody(req, maxBodyBytes);
      options.duplex = 'half';
    }
    const upstream = await fetchImpl(target, options);
    res.status(upstream.status);
    copyResponseHeaders(upstream.headers, res);
    if (!upstream.body || req.method === 'HEAD') {
      res.end();
      return;
    }
    Readable.fromWeb(upstream.body).on('error', () => res.destroy()).pipe(res);
  } catch (error) {
    if (!res.headersSent && (error?.code === 'REQUEST_BODY_TOO_LARGE' || req.__avzarBodyTooLarge === true)) {
      res.status(413).json({ code: 'request_body_too_large', message: 'The request body exceeds the allowed size.' });
      return;
    }
    if (!res.headersSent) res.status(502).json({ code: 'upstream_unavailable', message: 'The Avzar API is temporarily unavailable. Try again shortly.' });
    else res.destroy();
  }
}
