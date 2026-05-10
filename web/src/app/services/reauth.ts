// Step-up auth: when the API responds 401 with `error: "reauth_required"`,
// the user must re-prompt at ZITADEL with `max_age` so a fresh `auth_time`
// claim lands on the next access token. Returns whether the response was
// a reauth_required (caller skips its own error handling either way); the
// async redirect runs through a confirm dialog so the user understands
// why they're being bounced back to the IdP.
import type { ConfirmDialogService } from '../components/confirm-dialog';

export function handleReauthRequired(
  err: { status?: number; error?: unknown },
  returnTo: string,
  prompt?: {
    message: string;
    confirm: ConfirmDialogService;
    confirmLabel: string;
    cancelLabel: string;
  },
  maxAgeSeconds = 20,
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
  const target = `/auth/login?maxAge=${maxAgeSeconds}&returnTo=${ret}`;
  if (prompt) {
    prompt.confirm
      .show({
        message: prompt.message,
        confirmLabel: prompt.confirmLabel,
        cancelLabel: prompt.cancelLabel,
      })
      .then((ok) => {
        if (ok) window.location.href = target;
      });
  } else {
    window.location.href = target;
  }
  return true;
}
