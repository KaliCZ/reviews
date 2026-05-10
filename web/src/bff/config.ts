import dotenv from 'dotenv';

export interface BffConfig {
  apiUrl: string;
  port: number;
  sessionSecret: string;
  redisUrl: string;
  issuerPublic: string;
  issuerInternal: string;
  clientId: string | undefined;
  clientSecret: string | undefined;
}

/**
 * Three sources, in priority order:
 *   1. ZITADEL_ENV_FILE — Aspire writes a per-resource path here.
 *   2. /run/secrets/zitadel.env — docker compose bind-mount.
 *   3. ../infra/zitadel/.app-secrets/zitadel.env — host path for `npm run dev`,
 *      resolved from web/ (cwd of `npm --prefix web start`).
 *
 * Missing files are silently ignored by dotenv.
 */
export function loadConfig(): BffConfig {
  dotenv.config({ path: process.env['ZITADEL_ENV_FILE'] });
  dotenv.config({ path: '/run/secrets/zitadel.env' });
  dotenv.config({ path: '../infra/zitadel/.app-secrets/zitadel.env' });

  const issuerPublic =
    process.env['ZITADEL_PUBLIC_URL'] ?? process.env['ZITADEL_ISSUER'] ?? 'http://localhost:8080';

  return Object.freeze({
    apiUrl: process.env['API_URL'] ?? 'http://localhost:5146',
    port: Number(process.env['PORT'] ?? 4000),
    sessionSecret: process.env['SESSION_SECRET'] ?? 'dev-only-rotate-in-prod',
    redisUrl: process.env['REDIS_URL'] ?? 'redis://localhost:6379',
    issuerPublic,
    issuerInternal: process.env['ZITADEL_INTERNAL_URL'] ?? issuerPublic,
    clientId: process.env['ZITADEL_CLIENT_ID'],
    clientSecret: process.env['ZITADEL_CLIENT_SECRET'],
  });
}
