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
 * docker compose `env_file:` requires the file to exist at compose-up but
 * zitadel-bootstrap writes zitadel.env at runtime. Try both the compose path
 * and the Aspire-supplied ZITADEL_ENV_FILE; missing files are ignored.
 */
export function loadConfig(): BffConfig {
  dotenv.config({ path: process.env['ZITADEL_ENV_FILE'] });
  dotenv.config({ path: '/run/secrets/zitadel.env' });

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
