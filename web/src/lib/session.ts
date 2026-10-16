import { cookies } from 'next/headers';
import type { CurrentUser } from './auth-types';

const API = (process.env.API_URL ?? 'http://localhost:8080').replace(/\/+$/, '');
const SESSION_COOKIE = 'tailoring.session';

/** Server-side: the signed-in user for this request, or null. Only the session cookie is sent to the API. */
export async function getSession(): Promise<CurrentUser | null> {
  const jar = await cookies();
  const c = jar.get(SESSION_COOKIE);
  if (!c) return null;
  const res = await fetch(`${API}/api/auth/me`, { headers: { cookie: `${SESSION_COOKIE}=${c.value}` }, cache: 'no-store' });
  if (res.status === 401 || res.status === 403) return null;
  if (!res.ok) throw new Error(`API ${res.status} for /api/auth/me`);
  return (await res.json()) as CurrentUser;
}
