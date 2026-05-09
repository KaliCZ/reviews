import type { Application, NextFunction, Request, Response } from 'express';
import { createProxyMiddleware } from 'http-proxy-middleware';
import type { Client } from 'openid-client';

// Pulls the express-session module augmentation (req.session.tokenSet) into
// every consumer of api-proxy via transitive import.
import './session';

export interface ApiProxyOptions {
  apiUrl: string;
  oidcClient: Client | null;
}

/**
 * Refresh an about-to-expire access token before forwarding the API call.
 * Concurrent requests on the same session race-but-don't-corrupt because
 * express-session serializes writes per session.
 *
 * Exported separately from the middleware so unit tests can drive its
 * decision tree directly without spinning up Express.
 */
export async function ensureFreshToken(req: Request, oidcClient: Client | null): Promise<void> {
  const ts = req.session.tokenSet;
  if (!ts || !oidcClient) return;
  const now = Math.floor(Date.now() / 1000);
  if (ts.expires_at && ts.expires_at - now > 60) return; // still fresh
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
    console.warn('[bff] token refresh failed; clearing session', err);
    req.session.destroy(() => {});
  }
}

/**
 * Mount the token-refresh middleware and the upstream API proxy on /api.
 *
 * pathFilter (not app.use mounting) preserves the `/api` prefix on the
 * forwarded URL so the upstream sees /api/products, not /products.
 */
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
          // Don't leak the BFF session cookie upstream — the API is
          // cookie-blind and shouldn't see them.
          proxyReq.removeHeader('cookie');
        },
      },
    }),
  );
}
