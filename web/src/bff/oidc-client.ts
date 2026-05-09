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
 * Compute the final OIDC endpoint set after swapping discovered host names.
 *
 * Browser-facing endpoints (authorize, end_session) MUST use the public URL
 * since the user's browser hits them directly. Server-to-server endpoints
 * (token, userinfo, jwks) MUST use the internal URL so we don't bounce
 * through ingress for every back-channel call. The `issuer` claim itself
 * stays public — it's what the API validates as the `iss` claim on tokens.
 *
 * Pure function — extracted from createOidcClient for testability.
 */
export function swapEndpoints(
  discoveredMetadata: Record<string, unknown>,
  issuerPublic: string,
  issuerInternal: string,
): SwappedEndpoints {
  const swap = (u: string | undefined): string | undefined => {
    if (!u) return u;
    // Idempotent: if it's already pointing at the internal host, leave it.
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
 * Discover OIDC metadata (via the internal URL so JWKS works inside docker)
 * and build a configured client. Returns null when client credentials are
 * missing or discovery fails — callers degrade gracefully (auth disabled).
 */
export async function createOidcClient(options: OidcClientOptions): Promise<Client | null> {
  const { issuerPublic, issuerInternal, clientId, clientSecret, port } = options;

  if (!clientId || !clientSecret) {
    console.warn(
      '[bff] ZITADEL_CLIENT_ID/SECRET not set — auth routes return 503 until zitadel-bootstrap finishes',
    );
    return null;
  }

  // ZITADEL routes by Host header (matches ZITADEL_EXTERNALDOMAIN), so
  // every server-to-server hit needs Host: <public-host> even when we're
  // dialing the internal docker DNS name. Patch the global openid-client
  // HTTP options to inject the header on every request.
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
