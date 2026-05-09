import { Issuer, custom, type Client } from 'openid-client';

export interface OidcClientOptions {
  issuerPublic: string;
  issuerInternal: string;
  clientId: string | undefined;
  clientSecret: string | undefined;
  port: number;
}

export interface SwappedEndpoints {
  issuer: string;
  authorization_endpoint: string;
  end_session_endpoint: string;
  token_endpoint: string | undefined;
  userinfo_endpoint: string | undefined;
  jwks_uri: string | undefined;
}

/**
 * Browser-facing endpoints (authorize, end_session) stay on the public URL;
 * server-to-server (token, userinfo, jwks) swap to internal so back-channel
 * calls don't bounce through ingress. `issuer` stays public so the API's
 * `iss` claim validation matches the token.
 */
export function swapEndpoints(
  discoveredMetadata: Record<string, unknown>,
  issuerPublic: string,
  issuerInternal: string,
): SwappedEndpoints {
  const swap = (u: string | undefined): string | undefined => {
    if (!u) return u;
    if (u.startsWith(issuerInternal)) return u;
    return u.replace(issuerPublic, issuerInternal);
  };

  return {
    issuer: issuerPublic,
    authorization_endpoint: `${issuerPublic}/oauth/v2/authorize`,
    end_session_endpoint: `${issuerPublic}/oidc/v1/end_session`,
    token_endpoint: swap(discoveredMetadata['token_endpoint'] as string | undefined),
    userinfo_endpoint: swap(discoveredMetadata['userinfo_endpoint'] as string | undefined),
    jwks_uri: swap(discoveredMetadata['jwks_uri'] as string | undefined),
  };
}

/**
 * Returns null when creds are missing or discovery fails so callers can
 * degrade gracefully (auth disabled, no crash).
 */
export async function createOidcClient(options: OidcClientOptions): Promise<Client | null> {
  const { issuerPublic, issuerInternal, clientId, clientSecret, port } = options;

  if (!clientId || !clientSecret) {
    console.warn(
      '[bff] ZITADEL_CLIENT_ID/SECRET not set — auth routes return 503 until zitadel-bootstrap finishes',
    );
    return null;
  }

  // ZITADEL routes by Host header; server-to-server hits dial the internal
  // docker DNS name but still need Host: <public-host> for vhost matching.
  const publicHost = new URL(issuerPublic).host;
  custom.setHttpOptionsDefaults({
    headers: { host: publicHost },
  });

  try {
    const discovered = await Issuer.discover(issuerInternal);
    const swapped = swapEndpoints(
      discovered.metadata as Record<string, unknown>,
      issuerPublic,
      issuerInternal,
    );
    const issuer = new Issuer({
      ...discovered.metadata,
      ...swapped,
    });
    return new issuer.Client({
      client_id: clientId,
      client_secret: clientSecret,
      redirect_uris: [`http://localhost:${port}/auth/callback`],
      post_logout_redirect_uris: [`http://localhost:${port}/`],
      response_types: ['code'],
    });
  } catch (err) {
    console.error('[bff] OIDC discovery failed; auth disabled', err);
    return null;
  }
}
