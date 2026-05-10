import RedisStore from 'connect-redis';
import type { RequestHandler } from 'express';
import session from 'express-session';
import { createClient } from 'redis';
import { logger } from './logger';

declare module 'express-session' {
  interface SessionData {
    tokenSet?: {
      access_token: string;
      refresh_token?: string;
      id_token?: string;
      expires_at?: number;
    };
    user?: { sub: string; name?: string; email?: string };
    // Unix-seconds timestamp of the user's last interactive sign-in. Set on
    // /auth/callback (which is the moment the user authenticated) and
    // forwarded to the API as `X-Auth-Time` so step-up checks have a
    // freshness signal even when the JWT access token doesn't carry the
    // OIDC `auth_time` claim. Refresh-token grants don't go through the
    // callback, so this stays pinned to the last real password prompt.
    authTime?: number;
    codeVerifier?: string;
    returnTo?: string;
  }
}

export async function createSessionMiddleware(
  redisUrl: string,
  sessionSecret: string,
): Promise<RequestHandler> {
  const redis = createClient({
    url: redisUrl,
    socket:
      redisUrl.startsWith('rediss://') && process.env['REDIS_TLS_INSECURE'] === 'true'
        ? { tls: true, rejectUnauthorized: false }
        : undefined,
  });
  redis.on('error', (err) => logger.error({ err }, 'redis error'));
  await redis.connect();

  return session({
    store: new RedisStore({ client: redis, prefix: 'sess:' }),
    secret: sessionSecret,
    resave: false,
    saveUninitialized: false,
    cookie: {
      httpOnly: true,
      sameSite: 'lax',
      secure: false, // flip on behind HTTPS in prod
      maxAge: 1000 * 60 * 60 * 8,
    },
  });
}
