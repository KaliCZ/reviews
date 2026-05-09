import { describe, expect, it } from 'vitest';
import { swapEndpoints } from './oidc-client';

describe('swapEndpoints', () => {
  const PUBLIC = 'https://login.example.com';
  const INTERNAL = 'http://zitadel:8080';

  const discovered = {
    issuer: PUBLIC,
    authorization_endpoint: `${PUBLIC}/oauth/v2/authorize-discovered`,
    end_session_endpoint: `${PUBLIC}/oidc/v1/end_session-discovered`,
    token_endpoint: `${PUBLIC}/oauth/v2/token`,
    userinfo_endpoint: `${PUBLIC}/oidc/v1/userinfo`,
    jwks_uri: `${PUBLIC}/oauth/v2/keys`,
  };

  it('forces browser-facing endpoints to the public URL (overrides discovery)', () => {
    const out = swapEndpoints(discovered, PUBLIC, INTERNAL);
    expect(out.authorization_endpoint).toBe(`${PUBLIC}/oauth/v2/authorize`);
    expect(out.end_session_endpoint).toBe(`${PUBLIC}/oidc/v1/end_session`);
  });

  it('rewrites server-to-server endpoints to the internal URL', () => {
    const out = swapEndpoints(discovered, PUBLIC, INTERNAL);
    expect(out.token_endpoint).toBe(`${INTERNAL}/oauth/v2/token`);
    expect(out.userinfo_endpoint).toBe(`${INTERNAL}/oidc/v1/userinfo`);
    expect(out.jwks_uri).toBe(`${INTERNAL}/oauth/v2/keys`);
  });

  it('keeps the issuer claim public so API-side iss validation still matches', () => {
    const out = swapEndpoints(discovered, PUBLIC, INTERNAL);
    expect(out.issuer).toBe(PUBLIC);
  });

  it('passes endpoints through unchanged when public and internal URLs match (local dev)', () => {
    const out = swapEndpoints(discovered, PUBLIC, PUBLIC);
    expect(out.token_endpoint).toBe(`${PUBLIC}/oauth/v2/token`);
    expect(out.userinfo_endpoint).toBe(`${PUBLIC}/oidc/v1/userinfo`);
    expect(out.jwks_uri).toBe(`${PUBLIC}/oauth/v2/keys`);
    expect(out.issuer).toBe(PUBLIC);
  });

  it('is idempotent when discovery already returned the internal host', () => {
    const alreadyInternal = {
      ...discovered,
      token_endpoint: `${INTERNAL}/oauth/v2/token`,
      userinfo_endpoint: `${INTERNAL}/oidc/v1/userinfo`,
      jwks_uri: `${INTERNAL}/oauth/v2/keys`,
    };
    const out = swapEndpoints(alreadyInternal, PUBLIC, INTERNAL);
    expect(out.token_endpoint).toBe(`${INTERNAL}/oauth/v2/token`);
    expect(out.userinfo_endpoint).toBe(`${INTERNAL}/oidc/v1/userinfo`);
    expect(out.jwks_uri).toBe(`${INTERNAL}/oauth/v2/keys`);
  });

  it('leaves missing optional endpoints undefined rather than synthesising them', () => {
    const sparse = {
      issuer: PUBLIC,
      token_endpoint: `${PUBLIC}/oauth/v2/token`,
    };
    const out = swapEndpoints(sparse, PUBLIC, INTERNAL);
    expect(out.userinfo_endpoint).toBeUndefined();
    expect(out.jwks_uri).toBeUndefined();
    expect(out.token_endpoint).toBe(`${INTERNAL}/oauth/v2/token`);
  });
});
