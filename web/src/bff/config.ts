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
 * Aspire and docker-compose both set REVIEWS_APP_SECRETS_DIR explicitly; bare
 * `npm run dev` falls through to the shared default under the user home dir,
 * which is where zitadel-bootstrap writes its OIDC outputs.
 */
export function loadConfig(): BffConfig {
  const secretsDir =
    process.env['REVIEWS_APP_SECRETS_DIR'] ??
    path.join(os.homedir(), '.reviews-dev', 'app-secrets');
  dotenv.config({ path: path.join(secretsDir, 'zitadel.env') });

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
