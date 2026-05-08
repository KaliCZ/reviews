#!/bin/sh
# zitadel-bootstrap — runs once after the zitadel container is healthy and
# provisions the OIDC application + a test human user. Idempotent: re-runs
# detect existing resources via ZITADEL's mgmt API and reuse them.
#
# Inputs (read from /zitadel-secrets, populated by zitadel start-from-init):
#   admin-pat.txt   — PAT for the bootstrap service account
#
# Outputs (written to /app-secrets):
#   zitadel.env     — ZITADEL_ISSUER / ZITADEL_CLIENT_ID / ZITADEL_CLIENT_SECRET
#
# Both paths are bind-mounted volumes; reset by `docker compose down -v` or
# by deleting `infra/zitadel/.secrets/` and `infra/zitadel/.app-secrets/`.

set -eu

# Skip if the env file already has values. To force a re-run: delete the file
# (or wipe the volume / .app-secrets directory).
if [ -f /app-secrets/zitadel.env ]; then
  echo "[bootstrap] /app-secrets/zitadel.env already present — skipping"
  exit 0
fi

PAT=$(cat /zitadel-secrets/admin-pat.txt)
Z=${ZITADEL_INTERNAL_URL:-http://zitadel:8080}
ISSUER=${ZITADEL_PUBLIC_URL:-http://localhost:8080}

# `zitadel ready` flips green well before the projection layer has caught up,
# so the first management calls can 404. A tiny retry loop covers the gap.
api() {
  url=$1; method=${2:-GET}; data=${3:-}
  for i in 1 2 3 4 5; do
    # ZITADEL routes by the Host header (matches ZITADEL_EXTERNALDOMAIN), so
    # we explicitly send Host:localhost:8080 even though we connect via the
    # internal docker DNS name. Without this it returns 404 from inside the
    # network because the request appears to come from a different vhost.
    if [ -n "$data" ]; then
      out=$(curl -fsS -X "$method" "$Z$url" \
        -H "Host: localhost:8080" \
        -H "Authorization: Bearer $PAT" \
        -H "Content-Type: application/json" \
        -d "$data") && { echo "$out"; return 0; }
    else
      out=$(curl -fsS -X "$method" "$Z$url" \
        -H "Host: localhost:8080" \
        -H "Authorization: Bearer $PAT" \
        -H "Content-Type: application/json") && { echo "$out"; return 0; }
    fi
    echo "[bootstrap] $method $url failed (attempt $i), retrying" >&2
    sleep 2
  done
  return 1
}

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
echo "[bootstrap] Looking up or creating OIDC app 'reviews-bff'"
APP_LIST=$(api "/management/v1/projects/$PID/apps/_search" POST \
  '{"queries":[{"nameQuery":{"name":"reviews-bff","method":"TEXT_QUERY_METHOD_EQUALS"}}]}')
APP_ID=$(echo "$APP_LIST" | sed -n 's/.*"id":"\([^"]*\)".*/\1/p' | head -n 1)

# devMode=true is what lets ZITADEL accept the http:// redirect URI; without
# it the call fails with "redirect uri is not https".
APP_BODY="{
  \"name\":\"reviews-bff\",
  \"redirectUris\":[\"http://localhost:4000/auth/callback\",\"http://localhost:4200/auth/callback\"],
  \"postLogoutRedirectUris\":[\"http://localhost:4000/\",\"http://localhost:4200/\"],
  \"responseTypes\":[\"OIDC_RESPONSE_TYPE_CODE\"],
  \"grantTypes\":[\"OIDC_GRANT_TYPE_AUTHORIZATION_CODE\",\"OIDC_GRANT_TYPE_REFRESH_TOKEN\"],
  \"appType\":\"OIDC_APP_TYPE_WEB\",
  \"authMethodType\":\"OIDC_AUTH_METHOD_TYPE_BASIC\",
  \"devMode\":true
}"

if [ -z "$APP_ID" ]; then
  APP_CREATE=$(api "/management/v1/projects/$PID/apps/oidc" POST "$APP_BODY")
  CID=$(echo "$APP_CREATE" | sed -n 's/.*"clientId":"\([^"]*\)".*/\1/p' | head -n 1)
  SEC=$(echo "$APP_CREATE" | sed -n 's/.*"clientSecret":"\([^"]*\)".*/\1/p' | head -n 1)
  echo "[bootstrap] Created OIDC app — client_id=$CID"
else
  # App already exists from a previous run, but our local state was wiped
  # (otherwise we'd have exited at the env-file check above). ZITADEL won't
  # show the existing client_secret a second time, so rotate it via
  # ResetClientSecret to land in a known state.
  echo "[bootstrap] OIDC app exists ($APP_ID); rotating client_secret"
  APP_DETAIL=$(api "/management/v1/projects/$PID/apps/$APP_ID")
  CID=$(echo "$APP_DETAIL" | sed -n 's/.*"clientId":"\([^"]*\)".*/\1/p' | head -n 1)
  RESET=$(api "/management/v1/projects/$PID/apps/$APP_ID/oidc_config/_secret" POST '{}')
  SEC=$(echo "$RESET" | sed -n 's/.*"clientSecret":"\([^"]*\)".*/\1/p' | head -n 1)
fi

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
echo "[bootstrap] Done. Test login: alice / Password1!"
