import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express, { type Application } from 'express';
import { join } from 'node:path';
import { registerApiProxy } from './bff/api-proxy';
import { registerAuthRoutes } from './bff/auth-routes';
import { loadConfig } from './bff/config';
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
      console.log(`Reviews BFF listening on http://localhost:${config.port}`);
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
