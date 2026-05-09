import type { Request } from 'express';
import type { Client } from 'openid-client';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ensureFreshToken } from './api-proxy';

// Helper: build a fake request with a writable session shape that matches
// what express-session augments in production. Tests treat it loosely as
// `unknown` rather than re-deriving the full express types.
interface FakeSession {
  tokenSet?: {
    access_token: string;
    refresh_token?: string;
    id_token?: string;
    expires_at?: number;
  };
  save: (cb: (err?: Error | null) => void) => void;
  destroy: (cb: (err?: Error | null) => void) => void;
}

function makeReq(session: Partial<FakeSession>): Request {
  const fullSession: FakeSession = {
    save: (cb) => cb(null),
    destroy: (cb) => cb(null),
    ...session,
  };
  return { session: fullSession } as unknown as Request;
}

function makeClient(refresh: ReturnType<typeof vi.fn>): Client {
  return { refresh } as unknown as Client;
}

describe('ensureFreshToken', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-01-01T00:00:00Z'));
  });

  it('does nothing when the session has no tokenSet', async () => {
    const refresh = vi.fn();
    const req = makeReq({});
    await ensureFreshToken(req, makeClient(refresh));
    expect(refresh).not.toHaveBeenCalled();
  });

  it('does nothing when oidcClient is null (auth disabled)', async () => {
    const req = makeReq({
      tokenSet: { access_token: 'a', refresh_token: 'r', expires_at: 1 },
    });
    await ensureFreshToken(req, null);
    // No throw, no error — and we can still call save/destroy if needed.
    expect(req.session.tokenSet?.access_token).toBe('a');
  });

  it('does not refresh when the token still has more than 60s of life', async () => {
    const refresh = vi.fn();
    const now = Math.floor(Date.now() / 1000);
    const req = makeReq({
      tokenSet: { access_token: 'a', refresh_token: 'r', expires_at: now + 120 },
    });
    await ensureFreshToken(req, makeClient(refresh));
    expect(refresh).not.toHaveBeenCalled();
  });

  it('refreshes when the token expires within the 60s grace window', async () => {
    const now = Math.floor(Date.now() / 1000);
    const refresh = vi.fn().mockResolvedValue({
      access_token: 'new-a',
      refresh_token: 'new-r',
      id_token: 'new-id',
      expires_at: now + 3600,
    });
    const req = makeReq({
      tokenSet: { access_token: 'a', refresh_token: 'r', id_token: 'id', expires_at: now + 30 },
    });
    await ensureFreshToken(req, makeClient(refresh));
    expect(refresh).toHaveBeenCalledWith('r');
    expect(req.session.tokenSet?.access_token).toBe('new-a');
    expect(req.session.tokenSet?.expires_at).toBe(now + 3600);
  });

  it('refreshes when expires_at is undefined (treats unknown expiry as stale)', async () => {
    // Mirrors the original implementation: the `ts.expires_at && ...` guard
    // short-circuits when expires_at is falsy, so the code falls through to
    // the refresh path as long as a refresh_token is present.
    const refresh = vi.fn().mockResolvedValue({
      access_token: 'new-a',
      refresh_token: 'new-r',
      expires_at: 9999999999,
    });
    const req = makeReq({
      tokenSet: { access_token: 'a', refresh_token: 'r' },
    });
    await ensureFreshToken(req, makeClient(refresh));
    expect(refresh).toHaveBeenCalledOnce();
  });

  it('skips refresh when no refresh_token is available, even on an expired token', async () => {
    const refresh = vi.fn();
    const req = makeReq({
      tokenSet: { access_token: 'a', expires_at: 1 },
    });
    await ensureFreshToken(req, makeClient(refresh));
    expect(refresh).not.toHaveBeenCalled();
    expect(req.session.tokenSet?.access_token).toBe('a');
  });

  it('persists the refreshed tokenSet via session.save', async () => {
    const now = Math.floor(Date.now() / 1000);
    const refresh = vi.fn().mockResolvedValue({
      access_token: 'new-a',
      refresh_token: 'new-r',
      id_token: 'new-id',
      expires_at: now + 3600,
    });
    const save = vi.fn((cb: (err?: Error | null) => void) => cb(null));
    const req = makeReq({
      tokenSet: { access_token: 'a', refresh_token: 'r', expires_at: now + 5 },
      save,
    });
    await ensureFreshToken(req, makeClient(refresh));
    expect(save).toHaveBeenCalledOnce();
    expect(req.session.tokenSet).toEqual({
      access_token: 'new-a',
      refresh_token: 'new-r',
      id_token: 'new-id',
      expires_at: now + 3600,
    });
  });

  it('falls back to the previous refresh_token / id_token if the IdP omits them', async () => {
    const now = Math.floor(Date.now() / 1000);
    const refresh = vi.fn().mockResolvedValue({
      access_token: 'new-a',
      // No refresh_token / id_token returned
      expires_at: now + 3600,
    });
    const req = makeReq({
      tokenSet: {
        access_token: 'a',
        refresh_token: 'r-original',
        id_token: 'id-original',
        expires_at: now + 5,
      },
    });
    await ensureFreshToken(req, makeClient(refresh));
    expect(req.session.tokenSet?.refresh_token).toBe('r-original');
    expect(req.session.tokenSet?.id_token).toBe('id-original');
  });

  it('destroys the session when refresh throws', async () => {
    const now = Math.floor(Date.now() / 1000);
    const refresh = vi.fn().mockRejectedValue(new Error('boom'));
    const destroy = vi.fn((cb: (err?: Error | null) => void) => cb(null));
    const req = makeReq({
      tokenSet: { access_token: 'a', refresh_token: 'r', expires_at: now + 5 },
      destroy,
    });
    await ensureFreshToken(req, makeClient(refresh));
    expect(destroy).toHaveBeenCalledOnce();
  });
});
