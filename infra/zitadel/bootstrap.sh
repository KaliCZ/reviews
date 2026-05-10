#!/bin/sh
# zitadel-bootstrap — runs once after the zitadel container is healthy and
# provisions the OIDC application + a test human user. Idempotent: re-runs
# detect existing resources via ZITADEL's mgmt API and reuse them.
#
# Inputs (read from /zitadel-secrets, populated by zitadel start-from-init):
#   admin-pat.txt   — PAT for the bootstrap service account
#
# Outputs (written to /app-secrets):
#   zitadel.env    — KEY=VALUE flat dotenv for the JS BFF
#   Auth__Audience — KeyPerFile entry for the .NET API (filename = config key
#                    with `__` standing in for `:`, picked up automatically by
#                    KeyPerFileConfigurationProvider). The issuer URL is a
#                    deployment-topology fact, not a bootstrap-discovered
#                    secret, so it's set as an env var by the orchestration
#                    layer (Aspire AppHost / docker-compose) — not here.
#
# Both paths are bind-mounted volumes; reset by `docker compose down -v` or
# by deleting the host directories (default `~/.reviews-dev/`).

set -eu

# Timestamped output so the dashboard log shows phase progress and timing.
log() { echo "[bootstrap $(date +%H:%M:%S)] $*"; }

# Print wipe-and-restart instructions and exit. Used for any unrecoverable
# state where ZITADEL's DB and the on-disk PAT have drifted out of sync.
fail_with_recovery() {
  echo ""
  echo "[bootstrap] ERROR: $1"
  echo ""
  echo "  ZITADEL's DB and /zitadel-secrets/admin-pat.txt are out of sync."
  echo "  Wipe both halves and restart so FirstInstance runs against an empty"
  echo "  DB and writes a fresh PAT."
  echo ""
  if [ -n "${WORKTREE_ID:-}" ]; then
    WID=$WORKTREE_ID
    echo "  Aspire — stop the AppHost (Ctrl+C), then:"
    echo ""
    echo "  PowerShell:"
    echo "      docker ps -aq --filter volume=reviews-aspire-postgres-${WID} | % { docker rm -f \$_ }"
    echo "      docker volume rm reviews-aspire-postgres-${WID}"
    echo "      Remove-Item -Recurse -Force \"\$env:USERPROFILE\\.reviews-dev\\aspire\\${WID}\""
    echo ""
    echo "  bash / zsh:"
    echo "      docker ps -aq --filter volume=reviews-aspire-postgres-${WID} | xargs -r docker rm -f"
    echo "      docker volume rm reviews-aspire-postgres-${WID}"
    echo "      rm -rf ~/.reviews-dev/aspire/${WID}/"
  else
    echo "  Compose:"
    echo ""
    echo "  PowerShell:"
    echo "      docker compose down -v"
    echo "      Remove-Item -Recurse -Force \"\$env:USERPROFILE\\.reviews-dev\\zitadel-secrets\""
    echo "      Remove-Item -Recurse -Force \"\$env:USERPROFILE\\.reviews-dev\\app-secrets\""
    echo "      docker compose up"
    echo ""
    echo "  bash / zsh:"
    echo "      docker compose down -v"
    echo "      rm -rf ~/.reviews-dev/zitadel-secrets ~/.reviews-dev/app-secrets"
    echo "      docker compose up"
  fi
  echo ""
  exit 1
}

log "Starting"
log "  ZITADEL_INTERNAL_URL = ${ZITADEL_INTERNAL_URL:-http://zitadel:8080}"
log "  ZITADEL_PUBLIC_URL   = ${ZITADEL_PUBLIC_URL:-http://localhost:8080}"
log "  WORKTREE_ID          = ${WORKTREE_ID:-<unset, compose mode>}"

Z=${ZITADEL_INTERNAL_URL:-http://zitadel:8080}
ISSUER=${ZITADEL_PUBLIC_URL:-http://localhost:8080}

# Two-phase readiness gate: ZITADEL writes the PAT during FirstInstance
# and starts the HTTP listener afterwards, so the bootstrap needs to wait
# for both before it can call the management API.

log "Phase 1: waiting for /zitadel-secrets/admin-pat.txt"
phase1_attempt=0
while [ ! -s /zitadel-secrets/admin-pat.txt ]; do
  phase1_attempt=$((phase1_attempt + 1))
  if [ "$phase1_attempt" -gt 30 ]; then
    fail_with_recovery "/zitadel-secrets/admin-pat.txt never appeared (waited 60s)."
  fi
  log "  attempt $phase1_attempt/30: PAT file not yet present, sleeping 2s"
  sleep 2
done
log "Phase 1: PAT file ready (after ${phase1_attempt} retries)"
PAT=$(cat /zitadel-secrets/admin-pat.txt)

# Any HTTP response (even 4xx/5xx) means the listener is bound; auth
# correctness is validated in phase 3.
log "Phase 2: waiting for ZITADEL HTTP listener at $Z/debug/ready"
phase2_attempt=0
while true; do
  phase2_attempt=$((phase2_attempt + 1))
  probe_code=$(curl -sS -o /dev/null -m 5 -w "%{http_code}" "$Z/debug/ready" 2>/dev/null || echo "000")
  if [ "$probe_code" != "000" ]; then
    log "Phase 2: HTTP listener responded $probe_code (after ${phase2_attempt} retries)"
    break
  fi
  if [ "$phase2_attempt" -ge 30 ]; then
    fail_with_recovery "ZITADEL HTTP listener at $Z never responded (waited 60s)."
  fi
  log "  attempt $phase2_attempt/30: connection refused, sleeping 2s"
  sleep 2
done

# Comma-separated OIDC redirect / logout URIs to register with the app.
# Defaults cover compose; Aspire passes its assigned web port via env.
BFF_REDIRECT_URIS=${BFF_REDIRECT_URIS:-http://localhost:4000/auth/callback,http://localhost:4200/auth/callback}
BFF_POST_LOGOUT_URIS=${BFF_POST_LOGOUT_URIS:-http://localhost:4000/,http://localhost:4200/}

# POSIX-only — runs in curlimages/curl's busybox sh.
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

# ZITADEL routes by Host header (matches EXTERNALDOMAIN + EXTERNALPORT),
# so calls via the internal DNS name still need the public Host set.
VHOST=${ISSUER#http://}
VHOST=${VHOST#https://}
VHOST=${VHOST%%/*}

# Phase 3: PAT acceptance. 401/403 means the on-disk PAT and ZITADEL's
# DB are out of sync (only recoverable by wipe + re-init); fast-fail with
# recovery instructions instead of grinding through cryptic 401s in api().
log "Phase 3: validating PAT against ZITADEL management API"
auth_attempt=0
while true; do
  auth_attempt=$((auth_attempt + 1))
  AUTH_HTTP=$(curl -s -o /dev/null -m 5 -w "%{http_code}" -X POST \
    "$Z/management/v1/projects/_search" \
    -H "Host: $VHOST" \
    -H "Authorization: Bearer $PAT" \
    -H "Content-Type: application/json" \
    -d '{}' || echo "000")
  case "$AUTH_HTTP" in
    401|403)
      fail_with_recovery "ZITADEL rejected the PAT (HTTP $AUTH_HTTP)."
      ;;
    2*)
      log "Phase 3: PAT accepted (HTTP $AUTH_HTTP, after ${auth_attempt} attempts)"
      break
      ;;
    *)
      if [ "$auth_attempt" -ge 5 ]; then
        fail_with_recovery "Unexpected HTTP $AUTH_HTTP from ZITADEL after 5 attempts."
      fi
      log "  attempt $auth_attempt/5: HTTP $AUTH_HTTP, retrying in 2s"
      sleep 2
      ;;
  esac
done

# Retry wrapper: ZITADEL's projection layer can lag readiness, so the first
# calls sometimes 404. 5 attempts × 2s covers the gap.
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
    log "$method $url failed (attempt $i), retrying" >&2
    sleep 2
  done
  return 1
}

# Issuer URL now comes from orchestration env; scrub any leftover file
# that would otherwise shadow it via KeyPerFile.
rm -f /app-secrets/Auth__IssuerUrl

# --- Smart skip ------------------------------------------------------------
# Skip if zitadel.env on disk still matches ZITADEL's live OIDC app. The
# cross-check catches state drift after a postgres wipe that left the
# host secrets dir intact.
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
    log "/app-secrets/zitadel.env matches ZITADEL state — skipping"
    exit 0
  fi
  log "/app-secrets/zitadel.env stale (ZITADEL was reset?) — recreating"
  rm -f /app-secrets/zitadel.env /app-secrets/Auth__Audience
fi

# --- Project ---------------------------------------------------------------
log "Phase 4: looking up or creating project 'reviews'"
PROJECT_LIST=$(api /management/v1/projects/_search POST \
  '{"queries":[{"nameQuery":{"name":"reviews","method":"TEXT_QUERY_METHOD_EQUALS"}}]}')
PID=$(echo "$PROJECT_LIST" | sed -n 's/.*"id":"\([^"]*\)".*/\1/p' | head -n 1)

if [ -z "$PID" ]; then
  CREATE=$(api /management/v1/projects POST '{"name":"reviews"}')
  PID=$(echo "$CREATE" | sed -n 's/.*"id":"\([^"]*\)".*/\1/p' | head -n 1)
  log "Created project $PID"
else
  log "Project $PID already exists"
fi

# --- OIDC app --------------------------------------------------------------
# Stale app from a prior run gets deleted and recreated — ZITADEL v2.71
# only exposes client_secret at create time, with no working rotation API.
APP_LIST=$(api "/management/v1/projects/$PID/apps/_search" POST \
  '{"queries":[{"nameQuery":{"name":"reviews-bff","method":"TEXT_QUERY_METHOD_EQUALS"}}]}')
APP_ID=$(echo "$APP_LIST" | sed -n 's/.*"id":"\([^"]*\)".*/\1/p' | head -n 1)
if [ -n "$APP_ID" ]; then
  log "Removing stale OIDC app $APP_ID"
  curl -fsS -X DELETE "$Z/management/v1/projects/$PID/apps/$APP_ID" \
    -H "Host: $VHOST" -H "Authorization: Bearer $PAT" > /dev/null
fi

REDIRECT_URIS_JSON=$(to_json_array "$BFF_REDIRECT_URIS")
POST_LOGOUT_URIS_JSON=$(to_json_array "$BFF_POST_LOGOUT_URIS")

# devMode=true lets ZITADEL accept http:// redirect URIs (dev only).
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

log "Phase 5: creating OIDC app 'reviews-bff'"
APP_CREATE=$(api "/management/v1/projects/$PID/apps/oidc" POST "$APP_BODY")
CID=$(echo "$APP_CREATE" | sed -n 's/.*"clientId":"\([^"]*\)".*/\1/p' | head -n 1)
SEC=$(echo "$APP_CREATE" | sed -n 's/.*"clientSecret":"\([^"]*\)".*/\1/p' | head -n 1)
log "Created OIDC app — client_id=$CID"

# --- Test user -------------------------------------------------------------
log "Phase 6: importing test user 'alice' (idempotent)"
api /management/v1/users/human/_import POST '{
  "userName":"alice",
  "profile":{"firstName":"Alice","lastName":"Tester"},
  "email":{"email":"alice@localhost","isEmailVerified":true},
  "password":"Password1!",
  "passwordChangeRequired":false
}' > /dev/null 2>&1 || log "alice already exists"

# --- Output ----------------------------------------------------------------
log "Phase 7: writing /app-secrets/zitadel.env"
mkdir -p /app-secrets
cat > /app-secrets/zitadel.env <<EOF
ZITADEL_ISSUER=$ISSUER
ZITADEL_CLIENT_ID=$CID
ZITADEL_CLIENT_SECRET=$SEC
EOF

# KeyPerFileConfigurationProvider: filename `Auth__Audience` becomes the
# IConfiguration key `Auth:Audience` for the .NET API.
log "Writing per-key secret files for the API"
printf "%s" "$CID" > /app-secrets/Auth__Audience

log "Done. Test login: alice / Password1!"
