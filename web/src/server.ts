import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import RedisStore from 'connect-redis';
import dotenv from 'dotenv';
import express, { type Application, type Request, type Response, type NextFunction } from 'express';
import session from 'express-session';
import { createProxyMiddleware } from 'http-proxy-middleware';
import { join } from 'node:path';
import { Issuer, custom, generators, type Client } from 'openid-client';
import { createClient } from 'redis';

// zitadel-bootstrap writes /run/secrets/zitadel.env at runtime, so we can't
// reference it via docker compose `env_file:` (which expects the file to
// exist at compose-up). Aspire points at a different path via ZITADEL_ENV_FILE
// (a host bind-mount). Try both; first file present wins, missing files are
// silently ignored. Loaded synchronously at module import — cheap, just file
// reads — so the env values are visible to the constants below.
dotenv.config({ path: process.env['ZITADEL_ENV_FILE'] });
dotenv.config({ path: '/run/secrets/zitadel.env' });

const apiUrl = process.env['API_URL'] ?? 'http://localhost:5146';
const port = Number(process.env['PORT'] ?? 4000);
const sessionSecret = process.env['SESSION_SECRET'] ?? 'dev-only-rotate-in-prod';
const redisUrl = process.env['REDIS_URL'] ?? 'redis://localhost:6379';
const issuerPublic =
  process.env['ZITADEL_PUBLIC_URL'] ?? process.env['ZITADEL_ISSUER'] ?? 'http://localhost:8080';
const issuerInternal = process.env['ZITADEL_INTERNAL_URL'] ?? issuerPublic;
const clientId = process.env['ZITADEL_CLIENT_ID'];
const clientSecret = process.env['ZITADEL_CLIENT_SECRET'];

const browserDistFolder = join(import.meta.dirname, '../browser');
const angularApp = new AngularNodeAppEngine();

declare module 'express-session' {
  interface SessionData {
    tokenSet?: { access_token: string; refresh_token?: string; id_token?: string; expires_at?: number };
    user?: { sub: string; name?: string; email?: string };
    codeVerifier?: string;
    returnTo?: string;
  }
}

// Lazy app builder: defers Redis connect and OIDC discovery to first request.
// Angular's SSR build imports this module to extract the request handler;
// doing connect-on-import would crash the build (and any cold-start in an
// environment where the deps aren't ready yet).
let appPromise: Promise<Application> | null = null;

function getApp(): Promise<Application> {
  if (!appPromise) appPromise = buildApp();
  return appPromise;
}

async function buildApp(): Promise<Application> {
  const app = express();
  app.set('trust proxy', 1);

  // --- Session store -----------------------------------------------------
  const redis = createClient({ url: redisUrl });
  redis.on('error', (err) => console.error('[bff] redis error', err));
  await redis.connect();

  app.use(
    session({
      store: new RedisStore({ client: redis, prefix: 'sess:' }),
      secret: sessionSecret,
      resave: false,
      saveUninitialized: false,
      cookie: {
        httpOnly: true,
        sameSite: 'lax',
        secure: false, // toggle on behind real HTTPS in prod
        maxAge: 1000 * 60 * 60 * 8, // 8h idle
      },
    }),
  );

  // --- OIDC client -------------------------------------------------------
  let oidcClient: Client | null = null;
  if (clientId && clientSecret) {
    // ZITADEL routes by Host header (matches ZITADEL_EXTERNALDOMAIN), so
    // every server-to-server hit needs Host: <public-host> even when we're
    // dialing the internal docker DNS name. Patch the global openid-client
    // HTTP options to inject the header on every request.
    const publicHost = new URL(issuerPublic).host;
    custom.setHttpOptionsDefaults({
      headers: { host: publicHost },
    });

    try {
      // Discover via the INTERNAL URL so the JWKS fetch succeeds inside the
      // docker network. Then override browser-facing endpoints to the
      // PUBLIC URL so redirects send the user somewhere they can reach.
      const discovered = await Issuer.discover(issuerInternal);
      const meta = discovered.metadata as Record<string, unknown>;
      const swap = (u: string | undefined): string | undefined =>
        u ? u.replace(issuerPublic, issuerInternal) : u;
      const issuer = new Issuer({
        ...discovered.metadata,
        issuer: issuerPublic,
        authorization_endpoint: `${issuerPublic}/oauth/v2/authorize`,
        end_session_endpoint: `${issuerPublic}/oidc/v1/end_session`,
        token_endpoint: swap(meta['token_endpoint'] as string | undefined),
        userinfo_endpoint: swap(meta['userinfo_endpoint'] as string | undefined),
        jwks_uri: swap(meta['jwks_uri'] as string | undefined),
      });
      oidcClient = new issuer.Client({
        client_id: clientId,
        client_secret: clientSecret,
        redirect_uris: [`http://localhost:${port}/auth/callback`],
        post_logout_redirect_uris: [`http://localhost:${port}/`],
        response_types: ['code'],
      });
    } catch (err) {
      console.error('[bff] OIDC discovery failed; auth disabled', err);
    }
  } else {
    console.warn(
      '[bff] ZITADEL_CLIENT_ID/SECRET not set — auth routes return 503 until zitadel-bootstrap finishes',
    );
  }

  // --- Auth routes -------------------------------------------------------
  app.get('/auth/login', (req: Request, res: Response): void => {
    if (!oidcClient) {
      res.status(503).send('Auth not configured');
      return;
    }
    const codeVerifier = generators.codeVerifier();
    req.session.codeVerifier = codeVerifier;
    req.session.returnTo =
      typeof req.query['returnTo'] === 'string' ? req.query['returnTo'] : '/';
    const url = oidcClient.authorizationUrl({
      scope: 'openid profile email offline_access',
      code_challenge: generators.codeChallenge(codeVerifier),
      code_challenge_method: 'S256',
    });
    res.redirect(url);
  });

  app.get('/auth/callback', async (req: Request, res: Response): Promise<void> => {
    if (!oidcClient) {
      res.status(503).send('Auth not configured');
      return;
    }
    try {
      const params = oidcClient.callbackParams(req);
      const tokenSet = await oidcClient.callback(
        `http://localhost:${port}/auth/callback`,
        params,
        { code_verifier: req.session.codeVerifier },
      );
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
      console.error('[bff] callback failed', err);
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

  // /auth/me — minimal profile data the SPA uses to render auth state.
  // Tokens stay strictly server-side; the SPA only sees who the user is.
  app.get('/auth/me', (req: Request, res: Response): void => {
    if (!req.session.user) {
      res.status(401).json({ authenticated: false });
      return;
    }
    res.json({ authenticated: true, user: req.session.user });
  });

  // --- Token attachment + refresh ----------------------------------------
  // Refresh an about-to-expire access token before forwarding the API call.
  // Concurrent requests on the same session race-but-don't-corrupt because
  // express-session serializes writes per session.
  async function ensureFreshToken(req: Request): Promise<void> {
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

  // Token attach + proxy: pathFilter (not app.use mounting) preserves the
  // `/api` prefix on the forwarded URL so the upstream sees /api/products,
  // not /products. The token-refresh hop runs first via a dedicated
  // middleware on the same prefix.
  app.use((req: Request, _res: Response, next: NextFunction) => {
    if (!req.url.startsWith('/api')) return next();
    ensureFreshToken(req).then(() => next()).catch(next);
  });

  app.use(
    createProxyMiddleware({
      target: apiUrl,
      pathFilter: '/api',
      changeOrigin: true,
      on: {
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

  // --- Static + Angular SSR ---------------------------------------------
  app.use(
    express.static(browserDistFolder, {
      maxAge: '1y',
      index: false,
      redirect: false,
    }),
  );

  app.use((req, res, next) => {
    angularApp
      .handle(req)
      .then((response) =>
        response ? writeResponseToNodeResponse(response, res) : next(),
      )
      .catch(next);
  });

  return app;
}

if (isMainModule(import.meta.url) || process.env['pm_id']) {
  getApp().then((app) =>
    app.listen(port, (error) => {
      if (error) throw error;
      console.log(`Reviews BFF listening on http://localhost:${port}`);
    }),
  );
}

// Angular SSR / serverless invocation entry. First request triggers the
// async build; subsequent requests reuse the cached promise. Express's
// callable form accepts node-shaped Request/Response at runtime; the @types
// signature narrows to express's own types, hence the cast.
export const reqHandler = createNodeRequestHandler(async (req, res, next) => {
  const app = await getApp();
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  return (app as any)(req, res, next);
});
