/** Portal links put the bearer token in the URL fragment so it is not sent in HTTP requests or referrers. */
export function portalTokenFromHash(hash: string): string | null {
  const token = hash.startsWith('#') ? hash.slice(1) : hash;
  if (!token || token.length > 4096 || /[\s#]/.test(token)) return null;
  return token;
}
