import os from 'node:os';
import path from 'node:path';
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
 * Four sources, in priority order:
 *   1. ZITADEL_ENV_FILE — Aspire writes a per-resource path here.
 *   2. ${REVIEWS_APP_SECRETS_DIR}/zitadel.env — explicit override.
 *   3. /run/secrets/zitadel.env — docker compose bind-mount.
 *   4. <home>/.reviews-dev/app-secrets/zitadel.env — shared default for
 *      `npm run dev`, same dir Aspire writes to.
 *
 * Missing files are silently ignored by dotenv. dotenv.config keeps already-set
 * vars, so earlier sources win.
 */
export function loadConfig(): BffConfig {
  dotenv.config({ path: process.env['ZITADEL_ENV_FILE'] });
  if (process.env['REVIEWS_APP_SECRETS_DIR']) {
    dotenv.config({ path: `${process.env['REVIEWS_APP_SECRETS_DIR']}/zitadel.env` });
  }
  dotenv.config({ path: '/run/secrets/zitadel.env' });
  dotenv.config({ path: path.join(os.homedir(), '.reviews-dev', 'app-secrets', 'zitadel.env') });

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
