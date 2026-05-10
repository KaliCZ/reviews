// Step-up auth: when the API responds 401 with `error: "reauth_required"`,
// the user must re-prompt at ZITADEL with `max_age` so a fresh `auth_time`
// claim lands on the next access token. Returns true if a redirect was
// initiated.
export function handleReauthRequired(
  err: { status?: number; error?: unknown },
  returnTo: string,
  maxAgeSeconds = 300,
): boolean {
  if (err.status !== 401) return false;
  const body = err.error;
  const code =
    body && typeof body === 'object' && 'error' in body
      ? (body as { error?: unknown }).error
      : null;
  if (code !== 'reauth_required') return false;
  if (typeof window === 'undefined') return false;
  const ret = encodeURIComponent(returnTo);
  window.location.href = `/auth/login?maxAge=${maxAgeSeconds}&returnTo=${ret}`;
  return true;
}
