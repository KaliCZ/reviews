import RedisStore from 'connect-redis';
import type { RequestHandler } from 'express';
import session from 'express-session';
import { createClient } from 'redis';

declare module 'express-session' {
  interface SessionData {
    tokenSet?: {
      access_token: string;
      refresh_token?: string;
      id_token?: string;
      expires_at?: number;
    };
    user?: { sub: string; name?: string; email?: string };
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
  redis.on('error', (err) => console.error('[bff] redis error', err));
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
