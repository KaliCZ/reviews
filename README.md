# Reviews

A product reviews platform: browse a catalog, read SSR-rendered product pages, sign in, then submit / edit / delete / vote on reviews.

- Real OIDC auth via ZITADEL, surfaced to the SPA through the **BFF pattern** so tokens never reach the browser.
- Mutating actions run through **durable Temporal workflows**, so async moderation (like approval process) is built in.
- Reads are **cached in Redis** with workflow-driven invalidation — no TTL guesswork on hot pages.

User-facing flows — sign-in, catalog browse, product page, paginated/sorted listings, submit, edit, delete, vote, image upload — are walked through in [docs/flows.md](docs/flows.md).

## Stack

- **API** — ASP.NET Core 10 (`backend/api/`), JWT-Bearer protected, rate-limited writes.
- **Worker** — .NET worker host running Temporal workflows + activities (`backend/worker/`).
- **Shared library** — workflow type definitions referenced by both API and worker (`backend/shared/`).
- **Infrastructure library** — EF Core `DbContext`, migrations, and the seeder (`backend/infrastructure/`).
- **Frontend** — Angular 21 with SSR + a Backend-For-Frontend layer in the same Express server (`web/`).
- **Cache** — Redis (cached review first-pages + BFF session store).
- **Database** — PostgreSQL (one server, separate databases for app, auth, and Temporal).
- **Auth** — ZITADEL via the BFF pattern: tokens stay server-side, browser holds an HTTP-only session cookie.
- **Blob storage** — Azurite locally for review images, real Azure Blob in production (Aspire's `RunAsEmulator` toggle).
- **Workflow engine** — Temporal (server + UI), backed by the shared Postgres.
- **Orchestration** — .NET Aspire (`backend/apphost/`) + Docker Compose.

## Prerequisites

- .NET 10 SDK
- Node 24
- Docker Desktop

## Setup

```bash
npm install
npm --prefix web install
```

## Three ways to run

### 1. Aspire (richest dev experience)

```bash
npm run aspire
```

Spins up all services with the Aspire dashboard at the URL printed on startup. You get a unified log/trace/metrics view, hot reload on the API and Angular, connection strings injected automatically.

### 2. `npm run dev` (no Aspire workload required)

```bash
npm run dev
```

Brings up Postgres, Redis, Azurite, ZITADEL, Temporal, and the Temporal UI via `docker compose up -d --wait`, then runs `dotnet watch` on `api` and `worker` plus `ng serve` on the host, all under `concurrently` with hot reload.

Stop the infra containers with `docker compose down`.

### 3. Docker Compose (zero-install demo path)

```bash
docker compose up --build
```

Builds and runs everything containerized. Reviewer needs only Docker.

After it boots (all links available in aspire dashboard):
- Frontend: <http://localhost:4000>
- API: <http://localhost:8081>
- ZITADEL Console: <http://localhost:8080> (admin login: `zitadel-admin@reviews.localhost` / `Password1!`)
- Temporal UI: <http://localhost:8233>
- Test user for the app: `alice@localhost` / `Password1!` — pre-verified, log straight in.

### Registering your own user

Self-registering through the sign-in flow lands you on a "enter the verification code" page. ZITADEL would normally email the code, but no SMTP is wired up locally — instead, ZITADEL logs the code as a fallback. Grab it from the container logs and paste it into the UI:

```bash
docker logs $(docker ps --format '{{.Names}}' | grep '^zitadel-' | grep -v bootstrap) 2>&1 \
  | grep -oE 'Code:[A-Z0-9]+' | tail -1
```

That returns the latest InitCode (`Code:XXXXXX`). Paste the six characters after `Code:` into the verification page and the OIDC login flow completes.

## Tests

- `npm test` — unit + integration tests (.NET + frontend Vitest).
- `npm run test:e2e` — Playwright against the full compose stack.

Backend integration tests use Testcontainers, so they hit a real Postgres / Redis / Azurite and an in-process Temporal dev server — no mocks. The Playwright e2e suite goes one further and signals Temporal directly via `@temporalio/client` to drive the moderation approval path end-to-end.

## Project structure

```
reviews/
├── backend/
│   ├── api/                .NET API (controllers, JwtBearer auth, rate limiting)
│   ├── worker/             Temporal worker (workflows + activities runtime)
│   ├── shared/             Workflow type definitions (referenced by api + worker)
│   ├── infrastructure/     EF Core: DbContext, entities, migrations, seeder
│   ├── apphost/            Aspire orchestration project
│   ├── service-defaults/   Shared OTel / health-check / service-discovery wiring
│   └── tests/              xUnit test projects
├── web/                    Angular SSR frontend + BFF (in the same Express server)
├── infra/
│   ├── postgres-init.sh    Creates the additional databases on first start
│   └── zitadel/
│       ├── steps.yaml      ZITADEL FirstInstance bootstrap (admin + service-account PAT)
│       └── bootstrap.sh    Provisions the OIDC app via mgmt API after first start
├── Reviews.slnx            .NET solution
├── docker-compose.yml      Containerized run path
└── package.json            Root scripts: dev, dev:infra, aspire, test, test:e2e
```

## Design notes

### Schema, migrations, and seed

The data model is owned by `Reviews.Infrastructure`. EF Core `DbContext` + migrations live in `backend/infrastructure/Migrations/`. 

In production, we'd build an EF bundle and migrate inside the CI/CD, so that application logic doesn't need to handle this type of stuff. But there's no pipeline for it yet.

The API runs `Database.MigrateAsync()` at startup. The seed runs immediately afterand inserts 10 products with mixed reviews — some carrying image URLs.

### Auth: ZITADEL via the BFF pattern

ZITADEL is the OIDC provider. The BFF pattern keeps tokens server-side: the browser holds an opaque session cookie, the BFF stores access/id/refresh tokens in Redis, the API validates Bearer JWTs on the BFF-proxied calls. Hop-by-hop walk in [docs/flows.md](docs/flows.md#1-signing-in).

The application's `AuthorId` is a Guid hashed from the ZITADEL `sub` claim, so the rest of the system stays Guid-keyed without depending on how the IdP shapes its IDs.

#### Bootstrapping ZITADEL

ZITADEL doesn't yet support project/app provisioning via its declarative YAML. So the bootstrap is two-phase: `infra/zitadel/steps.yaml` creates the org + admin + a service-account PAT on first start, then a one-shot `zitadel-bootstrap` container uses that PAT to create the OIDC app and writes the resulting client id/secret to a bind-mounted secrets dir the API and BFF read at startup.

To reset auth state: `docker compose down -v && rm -rf infra/zitadel/.secrets infra/zitadel/.app-secrets`.

### Public vs internal URLs

Inside docker, `localhost:8080` (the browser-visible ZITADEL URL) doesn't resolve to ZITADEL — that's the api/web container's own perspective. So we run with two URLs: `http://localhost:8080` is what ends up in the JWT `iss` claim and what the browser redirects to; `http://zitadel:8080` is what the API and BFF actually talk to for JWKS / token / userinfo. The token issuer the API validates against is the public one; metadata fetches go to the internal one.

### Rate limiting

ASP.NET Core's `RateLimiter` on write endpoints only — reads are cache-fronted. Partition key is `user_id|ip` so rotating IPs while signed in or signing in from one IP don't sidestep the limit. 30 writes/minute, per partition.

### Cloudflare Turnstile

The submit-review form gates on a Turnstile token, verified server-side. Dev uses Cloudflare's documented always-pass test keys; prod swaps in real ones via `Turnstile:SiteKey` / `Turnstile:SecretKey` config.

### Workflows

Mutating actions with a moderation gate go through Temporal. Two workflows in `backend/shared/Workflows/`:

| Workflow | Moderation gate |
|---|---|
| `SubmitReviewWorkflow` | Ratings 1, 2, 5 wait for `Approve`/`Reject`; 3 and 4 auto-approve |
| `EditReviewWorkflow` | Edits >1h after submission wait for `Approve`/`Reject` |

Voting and deletion are synchronous in the API request — single transactional update + cache `DEL`. Delete is gated by a fresh `auth_time` claim (≤ 5 min) so a stolen access token can't quietly remove a review; a stale token returns `401 reauth_required` and the SPA bounces through `/auth/login?maxAge=300` to re-prompt the user. All four write paths (submit / edit / delete / vote) require a Turnstile token. Cache invalidation is shared between sync handlers and workflow activities via `IReviewCacheInvalidator`.

The moderation surface today is "open the workflow in Temporal UI, send the signal." A real admin app or MCP-backed agent can swap in without changing the durable contract.

### Cache shape

Three Redis surfaces, all invalidated by the workflow that mutates them:

| Key | What it caches | TTL |
|---|---|---|
| `products:list` | Catalog list | 24 hours |
| `products:slug:{slug}` | Product metadata + average rating + review count | 24 hours |
| `reviews:slug:{slug}:page:1` | First page of reviews, default sort | 24 hours |

TTL is the upper bound — every entry is also evicted by the workflow that mutates the underlying data, so a fresh review surfaces immediately, not after a 24-hour wait. The 24h ceiling exists only to bound staleness if a workflow ever fails to invalidate.

Sorts, filters, and pages past 1 go straight to Postgres — caching their cross-product would explode the keyspace and the long-tail traffic doesn't justify it. Per-viewer fields (`MyVote`, `Mine`, `MyReviewId`) are stripped before caching and re-merged on read.

## Deferred for later milestones

- **Smarter cache invalidation on vote.** Today every vote invalidates `reviews:slug:{slug}:page:1`. Most votes can't actually change the first page — e.g. a vote on a review that isn't on page 1, or a score change that doesn't cross a sort boundary under the default sort. Add heuristics (is the voted review on page 1? does the new score reorder the page?) so we only invalidate when the cached page could legitimately change.
- **Moderator surface + roles.** Today moderators sign workflows in the Temporal UI. Either an admin SPA or an MCP server on the API so an agent can fan out moderation actions, gated by a `moderator` claim.
- **Serve review images via a CDN**, bypassing the API.
- **Postgres Row-Level Security** as a second authorization layer.
- **Per-review translation** — language detection at submit time, translation on demand.
- **Comment threads on reviews.** Author clarifications, brand-owner replies, shopper follow-ups.
