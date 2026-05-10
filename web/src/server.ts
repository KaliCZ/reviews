import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import { trace } from '@opentelemetry/api';
import express, { type Application, type NextFunction, type Request, type Response } from 'express';
import { join } from 'node:path';
import { registerApiProxy } from './bff/api-proxy';
import { registerAuthRoutes } from './bff/auth-routes';
import { loadConfig } from './bff/config';
import { logger } from './bff/logger';
import { createOidcClient } from './bff/oidc-client';
import { createSessionMiddleware } from './bff/session';

const config = loadConfig();
const browserDistFolder = join(import.meta.dirname, '../browser');
const angularApp = new AngularNodeAppEngine();

// Lazy: Angular's SSR build imports this module at build time, so Redis
// connect / OIDC discovery have to defer to first request.
let appPromise: Promise<Application> | null = null;

function getApp(): Promise<Application> {
  if (!appPromise) appPromise = buildApp();
  return appPromise;
}

async function buildApp(): Promise<Application> {
  const app = express();
  app.set('trust proxy', 1);

  app.use(await createSessionMiddleware(config.redisUrl, config.sessionSecret));

  // Tag the active server span with session/user identifiers so the dashboard
  // can group/filter all BFF spans for one user across requests. Runs after
  // the session middleware so req.session is populated.
  app.use((req: Request, _res: Response, next: NextFunction) => {
    const span = trace.getActiveSpan();
    if (span) {
      if (req.session?.id) span.setAttribute('session.id', req.session.id);
      const sub = req.session?.user?.sub;
      if (sub) span.setAttribute('enduser.id', sub);
    }
    next();
  });

  // One structured access log per request. Skips assets / SSR HTML so the
  // log feed mirrors the trace filter — only /api and /auth calls show up.
  app.use((req: Request, res: Response, next: NextFunction) => {
    if (!req.url.startsWith('/api') && !req.url.startsWith('/auth')) return next();
    const start = Date.now();
    res.on('finish', () => {
      logger.info(
        {
          method: req.method,
          path: req.url.split('?')[0],
          status: res.statusCode,
          duration_ms: Date.now() - start,
          session_id: req.session?.id,
          enduser_id: req.session?.user?.sub,
        },
        `${req.method} ${req.url.split('?')[0]} ${res.statusCode}`,
      );
    });
    next();
  });

  const oidcClient = await createOidcClient(config);
  registerAuthRoutes(app, oidcClient, config.port);
  registerApiProxy(app, { apiUrl: config.apiUrl, oidcClient });

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
      .then((response) => (response ? writeResponseToNodeResponse(response, res) : next()))
      .catch(next);
  });

  return app;
}

if (isMainModule(import.meta.url) || process.env['pm_id']) {
  getApp().then((app) =>
    app.listen(config.port, (error) => {
      if (error) throw error;
      logger.info({ port: config.port }, 'Reviews BFF listening');
    }),
  );
}

// Cast: express's @types narrow callable to express's own Request/Response,
// but the runtime accepts node-shaped req/res objects from Angular SSR.
export const reqHandler = createNodeRequestHandler(async (req, res, next) => {
  const app = await getApp();
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  return (app as any)(req, res, next);
});
