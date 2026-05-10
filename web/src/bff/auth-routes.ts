import type { Application, Request, Response } from 'express';
import { generators, type Client } from 'openid-client';
import { logger } from './logger';

// 503 when oidcClient is null so the SPA can boot with auth disabled.
export function registerAuthRoutes(
  app: Application,
  oidcClient: Client | null,
  port: number,
): void {
  app.get('/auth/login', (req: Request, res: Response): void => {
    if (!oidcClient) {
      res.status(503).send('Auth not configured');
      return;
    }
    const codeVerifier = generators.codeVerifier();
    req.session.codeVerifier = codeVerifier;
    req.session.returnTo = typeof req.query['returnTo'] === 'string' ? req.query['returnTo'] : '/';

    // OIDC `max_age` (seconds): the IdP must re-prompt the user when their
    // session is older than this. Powers the API's reauth_required step-up
    // for high-impact actions like delete.
    const maxAgeRaw = typeof req.query['maxAge'] === 'string' ? Number(req.query['maxAge']) : NaN;
    const maxAge = Number.isFinite(maxAgeRaw) && maxAgeRaw > 0 ? Math.floor(maxAgeRaw) : undefined;

    const params: Record<string, string | number> = {
      scope: 'openid profile email offline_access',
      code_challenge: generators.codeChallenge(codeVerifier),
      code_challenge_method: 'S256',
    };
    if (maxAge !== undefined) params['max_age'] = maxAge;
    const url = oidcClient.authorizationUrl(params);
    res.redirect(url);
  });

  app.get('/auth/callback', async (req: Request, res: Response): Promise<void> => {
    if (!oidcClient) {
      res.status(503).send('Auth not configured');
      return;
    }
    try {
      const params = oidcClient.callbackParams(req);
      const tokenSet = await oidcClient.callback(`http://localhost:${port}/auth/callback`, params, {
        code_verifier: req.session.codeVerifier,
      });
      const claims = tokenSet.claims();
      req.session.tokenSet = {
        access_token: tokenSet.access_token!,
        refresh_token: tokenSet.refresh_token,
        id_token: tokenSet.id_token,
        expires_at: tokenSet.expires_at,
      };
      req.session.user = {
        sub: String(claims.sub),
        name: typeof claims['name'] === 'string' ? claims['name'] : undefined,
        email: typeof claims['email'] === 'string' ? claims['email'] : undefined,
      };
      delete req.session.codeVerifier;
      const dest = req.session.returnTo ?? '/';
      delete req.session.returnTo;
      res.redirect(dest);
    } catch (err) {
      logger.error({ err }, 'OIDC callback failed');
      res.status(401).send('Authentication failed');
    }
  });

  app.get('/auth/logout', (req: Request, res: Response): void => {
    const idToken = req.session.tokenSet?.id_token;
    req.session.destroy(() => {
      res.clearCookie('connect.sid');
      if (oidcClient && idToken) {
        const url = oidcClient.endSessionUrl({
          id_token_hint: idToken,
          post_logout_redirect_uri: `http://localhost:${port}/`,
        });
        res.redirect(url);
      } else {
        res.redirect('/');
      }
    });
  });

  // Tokens stay server-side; only profile data crosses to the SPA.
  app.get('/auth/me', (req: Request, res: Response): void => {
    if (!req.session.user) {
      res.status(401).json({ authenticated: false });
      return;
    }
    res.json({ authenticated: true, user: req.session.user });
  });
}
