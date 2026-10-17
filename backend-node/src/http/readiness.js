/**
 * Check the currently authoritative ASP.NET API's database-aware health endpoint.
 * Node must not report ready while its upstream dependency is unavailable.
 */
export async function checkUpstreamReadiness(apiUrl, fetchImpl = fetch, timeoutMs = 2500) {
  if (!apiUrl) return { ready: false, reason: 'upstream_api_not_configured' };

  let healthUrl;
  try {
    healthUrl = new URL('/health', apiUrl);
    if (!['http:', 'https:'].includes(healthUrl.protocol)) {
      return { ready: false, reason: 'upstream_api_not_configured' };
    }
  } catch {
    return { ready: false, reason: 'upstream_api_not_configured' };
  }

  try {
    const response = await fetchImpl(healthUrl, {
      method: 'GET',
      headers: { accept: 'application/json, text/plain' },
      cache: 'no-store',
      redirect: 'error',
      signal: AbortSignal.timeout(timeoutMs),
    });
    if (!response.ok) return { ready: false, reason: 'upstream_api_unhealthy' };
    return { ready: true, reason: null };
  } catch {
    return { ready: false, reason: 'upstream_api_unavailable' };
  }
}
