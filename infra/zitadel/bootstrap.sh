#!/bin/sh
# zitadel-bootstrap — runs once after the zitadel container is healthy and
# provisions the OIDC application + a test human user. Idempotent: re-runs
# detect existing resources via ZITADEL's mgmt API and reuse them.
#
# Inputs (read from /zitadel-secrets, populated by zitadel start-from-init):
#   admin-pat.txt   — PAT for the bootstrap service account
#
# Outputs (written to /app-secrets):
#   zitadel.env     — KEY=VALUE flat dotenv for the JS BFF
#   Auth__IssuerUrl — KeyPerFile entries for the .NET API (one file per key,
#   Auth__Audience    filename = config key with `__` standing in for `:`).
#                     The api's KeyPerFileConfigurationProvider picks them up
#                     automatically.
#
# Both paths are bind-mounted volumes; reset by `docker compose down -v` or
# by deleting the host directories (default `~/.reviews-dev/`).

set -eu

# Compose's zitadel healthcheck waits for ZITADEL's FirstInstance to finish
# (which is when the PAT lands on disk). Aspire's WaitFor only blocks on the
# container starting, so bootstrap can race ahead of FirstInstance — wait for
# the file explicitly.
for i in $(seq 1 60); do
  [ -s /zitadel-secrets/admin-pat.txt ] && break
  echo "[bootstrap] waiting for /zitadel-secrets/admin-pat.txt (attempt $i)"
  sleep 2
done
PAT=$(cat /zitadel-secrets/admin-pat.txt)
Z=${ZITADEL_INTERNAL_URL:-http://zitadel:8080}
ISSUER=${ZITADEL_PUBLIC_URL:-http://localhost:8080}

# OIDC redirect URIs the BFF will use, registered with the OIDC app below.
# Comma-separated so each mode can pass however many it needs:
#   - compose: 4000 (the web container) AND 4200 (npm run dev's local web)
#   - Aspire: whatever random port AppHost assigned to web
# Defaults preserve compose's two-URI list so compose passes nothing extra.
BFF_REDIRECT_URIS=${BFF_REDIRECT_URIS:-http://localhost:4000/auth/callback,http://localhost:4200/auth/callback}
BFF_POST_LOGOUT_URIS=${BFF_POST_LOGOUT_URIS:-http://localhost:4000/,http://localhost:4200/}

# Convert a comma-separated list into a JSON string array. POSIX shell, no
# bashisms — runs in curlimages/curl's busybox sh.
to_json_array() {
  saved_ifs=$IFS
  IFS=','
  result='['
  sep=''
  for item in $1; do
    result="${result}${sep}\"${item}\""
    sep=','
  done
  IFS=$saved_ifs
  echo "${result}]"
}

# ZITADEL routes by the Host header (matches ZITADEL_EXTERNALDOMAIN +
# ZITADEL_EXTERNALPORT), so requests sent to the internal docker DNS name
# need their Host header rewritten to the public authority. Derive it from
# ZITADEL_PUBLIC_URL so this script works for compose (localhost:8080) and
# for Aspire (random per-AppHost port) without extra config.
VHOST=${ISSUER#http://}
VHOST=${VHOST#https://}
VHOST=${VHOST%%/*}

# `zitadel ready` flips green well before the projection layer has caught up,
# so the first management calls can 404. A tiny retry loop covers the gap.
api() {
  url=$1; method=${2:-GET}; data=${3:-}
  for i in 1 2 3 4 5; do
    if [ -n "$data" ]; then
      out=$(curl -fsS -X "$method" "$Z$url" \
        -H "Host: $VHOST" \
        -H "Authorization: Bearer $PAT" \
        -H "Content-Type: application/json" \
        -d "$data") && { echo "$out"; return 0; }
    else
      out=$(curl -fsS -X "$method" "$Z$url" \
        -H "Host: $VHOST" \
        -H "Authorization: Bearer $PAT" \
        -H "Content-Type: application/json") && { echo "$out"; return 0; }
    fi
    echo "[bootstrap] $method $url failed (attempt $i), retrying" >&2
    sleep 2
  done
  return 1
}

# --- Smart skip ------------------------------------------------------------
# If a previous run left zitadel.env on disk AND ZITADEL still has a matching
# OIDC app, nothing to do. The cross-check guards against the case where the
# host's .app-secrets directory survived a postgres-volume wipe (e.g.
# switching between Aspire and compose, or between two compose stacks) — we
# would otherwise hand the BFF a client_id that ZITADEL never created.
if [ -f /app-secrets/zitadel.env ]; then
  STORED_CID=$(sed -n 's/^ZITADEL_CLIENT_ID=//p' /app-secrets/zitadel.env)
  PROJECT_LIST=$(api /management/v1/projects/_search POST \
    '{"queries":[{"nameQuery":{"name":"reviews","method":"TEXT_QUERY_METHOD_EQUALS"}}]}' || true)
  CHECK_PID=$(echo "$PROJECT_LIST" | sed -n 's/.*"id":"\([^"]*\)".*/\1/p' | head -n 1)
  LIVE_CID=
  if [ -n "$CHECK_PID" ]; then
    APP_LIST=$(api "/management/v1/projects/$CHECK_PID/apps/_search" POST \
      '{"queries":[{"nameQuery":{"name":"reviews-bff","method":"TEXT_QUERY_METHOD_EQUALS"}}]}' || true)
    LIVE_CID=$(echo "$APP_LIST" | sed -n 's/.*"clientId":"\([^"]*\)".*/\1/p' | head -n 1)
  fi
  if [ -n "$STORED_CID" ] && [ "$STORED_CID" = "$LIVE_CID" ]; then
    echo "[bootstrap] /app-secrets/zitadel.env matches ZITADEL state — skipping"
    exit 0
  fi
  echo "[bootstrap] /app-secrets/zitadel.env stale (ZITADEL was reset?) — recreating"
  rm -f /app-secrets/zitadel.env /app-secrets/Auth__IssuerUrl /app-secrets/Auth__Audience
fi

# --- Project ---------------------------------------------------------------
echo "[bootstrap] Looking up or creating project 'reviews'"
PROJECT_LIST=$(api /management/v1/projects/_search POST \
  '{"queries":[{"nameQuery":{"name":"reviews","method":"TEXT_QUERY_METHOD_EQUALS"}}]}')
PID=$(echo "$PROJECT_LIST" | sed -n 's/.*"id":"\([^"]*\)".*/\1/p' | head -n 1)

if [ -z "$PID" ]; then
  CREATE=$(api /management/v1/projects POST '{"name":"reviews"}')
  PID=$(echo "$CREATE" | sed -n 's/.*"id":"\([^"]*\)".*/\1/p' | head -n 1)
  echo "[bootstrap] Created project $PID"
else
  echo "[bootstrap] Project $PID already exists"
fi

# --- OIDC app --------------------------------------------------------------
# Reaching here means the smart-skip above didn't match, so any pre-existing
# app belongs to a stale ZITADEL state. ZITADEL only exposes client_secret at
# create time (no working rotation endpoint in v2.71), so the only way to
# land in a known-good state is to remove and recreate.
APP_LIST=$(api "/management/v1/projects/$PID/apps/_search" POST \
  '{"queries":[{"nameQuery":{"name":"reviews-bff","method":"TEXT_QUERY_METHOD_EQUALS"}}]}')
APP_ID=$(echo "$APP_LIST" | sed -n 's/.*"id":"\([^"]*\)".*/\1/p' | head -n 1)
if [ -n "$APP_ID" ]; then
  echo "[bootstrap] Removing stale OIDC app $APP_ID"
  curl -fsS -X DELETE "$Z/management/v1/projects/$PID/apps/$APP_ID" \
    -H "Host: $VHOST" -H "Authorization: Bearer $PAT" > /dev/null
fi

REDIRECT_URIS_JSON=$(to_json_array "$BFF_REDIRECT_URIS")
POST_LOGOUT_URIS_JSON=$(to_json_array "$BFF_POST_LOGOUT_URIS")

# devMode=true is what lets ZITADEL accept the http:// redirect URI; without
# it the call fails with "redirect uri is not https".
APP_BODY="{
  \"name\":\"reviews-bff\",
  \"redirectUris\":$REDIRECT_URIS_JSON,
  \"postLogoutRedirectUris\":$POST_LOGOUT_URIS_JSON,
  \"responseTypes\":[\"OIDC_RESPONSE_TYPE_CODE\"],
  \"grantTypes\":[\"OIDC_GRANT_TYPE_AUTHORIZATION_CODE\",\"OIDC_GRANT_TYPE_REFRESH_TOKEN\"],
  \"appType\":\"OIDC_APP_TYPE_WEB\",
  \"authMethodType\":\"OIDC_AUTH_METHOD_TYPE_BASIC\",
  \"devMode\":true,
  \"accessTokenType\":\"OIDC_TOKEN_TYPE_JWT\",
  \"accessTokenRoleAssertion\":true,
  \"idTokenRoleAssertion\":true,
  \"idTokenUserinfoAssertion\":true
}"

echo "[bootstrap] Creating OIDC app 'reviews-bff'"
APP_CREATE=$(api "/management/v1/projects/$PID/apps/oidc" POST "$APP_BODY")
CID=$(echo "$APP_CREATE" | sed -n 's/.*"clientId":"\([^"]*\)".*/\1/p' | head -n 1)
SEC=$(echo "$APP_CREATE" | sed -n 's/.*"clientSecret":"\([^"]*\)".*/\1/p' | head -n 1)
echo "[bootstrap] Created OIDC app — client_id=$CID"

# --- Test user -------------------------------------------------------------
echo "[bootstrap] Importing test user 'alice' (idempotent)"
api /management/v1/users/human/_import POST '{
  "userName":"alice",
  "profile":{"firstName":"Alice","lastName":"Tester"},
  "email":{"email":"alice@localhost","isEmailVerified":true},
  "password":"Password1!",
  "passwordChangeRequired":false
}' > /dev/null 2>&1 || echo "[bootstrap] alice already exists"

# --- Output ----------------------------------------------------------------
echo "[bootstrap] Writing /app-secrets/zitadel.env"
mkdir -p /app-secrets
cat > /app-secrets/zitadel.env <<EOF
ZITADEL_ISSUER=$ISSUER
ZITADEL_CLIENT_ID=$CID
ZITADEL_CLIENT_SECRET=$SEC
EOF

# Per-key files for the .NET API (KeyPerFileConfigurationProvider). Filenames
# encode the IConfiguration key with `__` standing in for `:` — so
# `Auth__IssuerUrl` surfaces as `Auth:IssuerUrl`. RequireHttps is left to the
# api's appsettings (true in prod, overridden to false in compose); we don't
# write it here so the framework's normal precedence stays predictable.
echo "[bootstrap] Writing per-key secret files for the API"
printf "%s" "$ISSUER" > /app-secrets/Auth__IssuerUrl
printf "%s" "$CID"    > /app-secrets/Auth__Audience

echo "[bootstrap] Done. Test login: alice / Password1!"
