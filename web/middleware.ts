import { NextResponse, type NextRequest } from 'next/server';

const BASE_DOMAIN = (process.env.BASE_DOMAIN ?? 'localhost').toLowerCase();
const API = process.env.API_URL ?? 'http://localhost:8080';
const MEDIA_ORIGIN = (() => { try { return new URL(process.env.NEXT_PUBLIC_API_PUBLIC_URL ?? API).origin; } catch { return ''; } })();
const TURNSTILE = !!process.env.NEXT_PUBLIC_TURNSTILE_SITE_KEY;
const isDev = process.env.NODE_ENV !== 'production';

/** Per-request nonce so inline scripts Next needs are allowed and nothing else is. */
function csp(nonce: string): string {
  const cf = TURNSTILE ? ' https://challenges.cloudflare.com' : '';
  return [
    "default-src 'self'",
    `script-src 'self' 'nonce-${nonce}' 'strict-dynamic'${cf}${isDev ? " 'unsafe-eval'" : ''}`,
    // Colours and sizes come from validated values (#RRGGBB, bounded numbers) as inline style ATTRIBUTES only; style elements stay locked down.
    "style-src 'self' https://fonts.googleapis.com" + (isDev ? " 'unsafe-inline'" : ''),
    "style-src-attr 'unsafe-inline'",
    "font-src 'self' https://fonts.gstatic.com",
    `img-src 'self' data: ${MEDIA_ORIGIN}`.trim(),
    "connect-src 'self'",
    `frame-src ${TURNSTILE ? 'https://challenges.cloudflare.com' : "'none'"}`,
    "frame-ancestors 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "object-src 'none'",
  ].join('; ');
}

async function resolveCustomHost(host: string): Promise<string | null> {
  try {
    const res = await fetch(`${API}/public/resolve?host=${encodeURIComponent(host)}`, { next: { revalidate: 60 } });
    if (!res.ok) return null;
    const body = (await res.json()) as { slug?: string };
    return body.slug ?? null;
  } catch {
    return null;
  }
}

export async function middleware(req: NextRequest) {
  const nonce = btoa(crypto.randomUUID());
  const host = (req.headers.get('host') ?? '').split(':')[0]!.toLowerCase();
  const path = req.nextUrl.pathname;

  // Which site does this host belong to? {slug}.BASE_DOMAIN, or a verified custom domain.
  let slug: string | null = null;
  const internal = path.startsWith('/sites/') || path.startsWith('/preview/') || path.startsWith('/api/') || path.startsWith('/_next/');
  if (!internal && host !== BASE_DOMAIN) {
    if (host.endsWith(`.${BASE_DOMAIN}`)) slug = host.slice(0, -(BASE_DOMAIN.length + 1));
    else if (host !== 'localhost') slug = await resolveCustomHost(host);
  }

  const headers = new Headers(req.headers);
  headers.delete('x-site-root'); // never trust a client-supplied value; it is only set below for tenant hosts
  headers.set('x-nonce', nonce);
  headers.set('content-security-policy', csp(nonce)); // Next reads the nonce from here and applies it to its own scripts

  let res: NextResponse;
  if (slug) {
    headers.set('x-site-root', '1'); // links on this host are root-relative (/about), not /sites/slug/about
    const url = req.nextUrl.clone();
    url.pathname = `/sites/${slug}${path === '/' ? '' : path}`;
    res = NextResponse.rewrite(url, { request: { headers } });
  } else {
    res = NextResponse.next({ request: { headers } });
  }
  res.headers.set('content-security-policy', csp(nonce));
  return res;
}

export const config = {
  matcher: [{ source: '/((?!_next/static|_next/image|favicon.ico).*)', missing: [{ type: 'header', key: 'next-router-prefetch' }, { type: 'header', key: 'purpose', value: 'prefetch' }] }],
};
