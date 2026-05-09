import type { Application, NextFunction, Request, Response } from 'express';
import { createProxyMiddleware } from 'http-proxy-middleware';
import type { Client } from 'openid-client';
import { logger } from './logger';

// Side-effect import: pulls the req.session.tokenSet module augmentation.
import './session';

export interface ApiProxyOptions {
  apiUrl: string;
  oidcClient: Client | null;
}

// express-session serializes writes per session, so concurrent refreshes
// race-but-don't-corrupt. Exported for unit tests.
export async function ensureFreshToken(req: Request, oidcClient: Client | null): Promise<void> {
  const ts = req.session.tokenSet;
  if (!ts || !oidcClient) return;
  const now = Math.floor(Date.now() / 1000);
  if (ts.expires_at && ts.expires_at - now > 60) return;
  if (!ts.refresh_token) return;
  try {
    const refreshed = await oidcClient.refresh(ts.refresh_token);
    req.session.tokenSet = {
      access_token: refreshed.access_token!,
      refresh_token: refreshed.refresh_token ?? ts.refresh_token,
      id_token: refreshed.id_token ?? ts.id_token,
      expires_at: refreshed.expires_at,
    };
    await new Promise<void>((resolve, reject) =>
      req.session.save((err) => (err ? reject(err) : resolve())),
    );
  } catch (err) {
    logger.warn({ err }, 'token refresh failed; clearing session');
    req.session.destroy(() => {});
  }
}

// pathFilter (not app.use mounting) keeps the `/api` prefix on forwarded URLs.
export function registerApiProxy(app: Application, options: ApiProxyOptions): void {
  const { apiUrl, oidcClient } = options;

  app.use((req: Request, _res: Response, next: NextFunction) => {
    if (!req.url.startsWith('/api')) return next();
    ensureFreshToken(req, oidcClient)
      .then(() => next())
      .catch(next);
  });

  app.use(
    createProxyMiddleware({
      target: apiUrl,
      pathFilter: '/api',
      changeOrigin: true,
      on: {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        proxyReq: (proxyReq, req: any) => {
          const ts = req.session?.tokenSet;
          if (ts?.access_token) {
            proxyReq.setHeader('Authorization', `Bearer ${ts.access_token}`);
          }
          // BFF session cookie stays at the BFF.
          proxyReq.removeHeader('cookie');
        },
      },
    }),
  );
}
