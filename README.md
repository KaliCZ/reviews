# Reviews

A product reviews platform — viewing, browsing, submitting, rating, editing, and deleting reviews — wired end-to-end through a real OIDC auth flow, durable Temporal workflows, EF Core migrations, blob-stored review images, and rate-limited write endpoints.

The four user flows the product is built around are described in [docs/flows.md](docs/flows.md).

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

After it boots:
- Frontend: <http://localhost:4000>
- API: <http://localhost:8081>
- ZITADEL Console: <http://localhost:8080> (admin login: `zitadel-admin@reviews.localhost` / `Password1!`)
- Temporal UI: <http://localhost:8233>
- Test user for the app: `alice@localhost` / `Password1!`

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
└── package.json            Root scripts: dev, dev:infra, aspire, e2e
```

## Design notes

### Schema, migrations, and seed

The data model is owned by `Reviews.Infrastructure`. EF Core `DbContext` + migrations live in `backend/infrastructure/Migrations/`. The API runs `Database.MigrateAsync()` at startup, wrapped in a Postgres `pg_advisory_lock` so it's safe under multiple replicas — first wins, others see "no pending migrations" and continue. In production you'd flip `Reviews:AutoApply=false` and run a deploy-time bundle; in dev there's no pipeline and auto-apply is fine.

The seed runs immediately after, in the same lock-protected step. It inserts 10 products with deliberately mixed averages (some products average ~4.5+, others ~1.7-2.3, the rest middling), plus ~40 reviews — some carrying image URLs. **Image bytes themselves are downloaded from picsum.photos at first boot and uploaded to Azurite**, then served back through the API at `/api/images/{path}` so the URL is environment-agnostic (works through SSR proxy, in compose, in prod with real Azure Blob — no hardcoded host names).

The worker doesn't run migrations. It depends on the API's `/health` (Aspire `WaitFor`, compose `service_healthy`) so by the time it queries, the schema is ready. One owner, one path — no race.

### Auth: ZITADEL via the BFF pattern

OWASP-recommended SPA shape as of 2025: tokens stay server-side, the browser only ever holds an opaque session cookie.

- **Browser** ↔ **Angular SSR / BFF**: HTTP-only session cookie, OIDC code-flow redirects via `/auth/login`, `/auth/callback`, `/auth/logout`, identity exposure via `/auth/me`. Sessions live in Redis (the same Redis the rest of the app uses). Token refresh happens server-side, transparent to the browser.
- **BFF** ↔ **.NET API**: the `/api/*` proxy strips the cookie, attaches `Authorization: Bearer <access_token>` from the session, forwards. The API has no idea cookies exist — it's a pure JWT resource server.
- **API** ↔ **ZITADEL**: standard `JwtBearer` validation against ZITADEL's JWKS, fetched from `.well-known/openid-configuration`. The `sub` claim is hashed (SHA-256 → first 16 bytes → Guid) into the application's `AuthorId` so the rest of the system stays Guid-keyed without knowing how the IdP shapes its IDs.

#### How ZITADEL gets its OIDC app

ZITADEL doesn't (yet) support project/app provisioning via its declarative `--steps` YAML. So the bootstrap is two-phase:

1. **`infra/zitadel/steps.yaml`** runs on first start of an empty zitadel database (`zitadel start-from-init --steps /steps.yaml`). Creates the default org, the human admin, and a `bootstrap` service-account whose PAT is written to a shared volume.
2. **`zitadel-bootstrap`** (a one-shot init container) waits for ZITADEL to be healthy, reads the PAT, calls the management API to create-or-find the project + OIDC app, and writes `ZITADEL_ISSUER` / `ZITADEL_CLIENT_ID` / `ZITADEL_CLIENT_SECRET` to a second shared location. The api and web containers wait on this completing successfully and read the env file at startup (`DotEnvLoader` in C#, `dotenv` in the BFF).

Both shared locations are bind-mounted host directories (`infra/zitadel/.secrets/`, `infra/zitadel/.app-secrets/`), which means the same provisioning is shared between the compose and Aspire run paths, and you can read the resulting credentials from the host. Reset by deleting both directories or running `docker compose down -v`.

### Public vs internal URLs

Inside docker, `localhost:8080` (the user-visible ZITADEL URL) doesn't resolve to the IdP — that's the api/web container's own perspective. Two URLs cover this:

- **Public**: `http://localhost:8080` — what ends up in the JWT `iss` claim and what the browser redirects to. The .NET API validates token issuers against this string.
- **Internal**: `http://zitadel:8080` — what the api uses to fetch JWKS, and what the BFF uses for code-exchange / userinfo / refresh calls.

The BFF builds a custom OIDC `Issuer` object that splits these per-endpoint; the API's `JwtBearer` config sets `MetadataAddress` to internal and `ValidIssuer` to public.

### Rate limiting

ASP.NET Core's `RateLimiter` middleware on the write endpoints only — reads are cache-fronted and uncapped. The partition key is `user_id|ip` so an attacker can't sidestep by rotating IPs while logged in, or by logging in/out from one IP. 30 writes/minute per partition; rejected requests get `429`.

### Cloudflare Turnstile

The submit-review form gates on a Turnstile token, verified server-side by the API against `challenges.cloudflare.com/turnstile/v0/siteverify`. Dev uses Cloudflare's documented test keys (always-passes site key + always-accepts secret), so no real Cloudflare account is needed. Production swaps both via `Turnstile:SiteKey` / `Turnstile:SecretKey` config — nothing else changes.

### Workflows

The mutating flows from `docs/flows.md` map onto four Temporal workflows in `backend/shared/Workflows/`:

| Workflow | Moderation gate | Activities |
|---|---|---|
| `SubmitReviewWorkflow` | Ratings 1, 2, 5 wait for `Approve`/`Reject` signal; 3 and 4 auto-persist | `PersistReview`, `RefreshFirstPageCache` |
| `EditReviewWorkflow` | Edits to reviews >1h old wait for moderator signal | `LookupReview`, `ApplyReviewEdit`, `RefreshFirstPageCache` |
| `DeleteReviewWorkflow` | Same 1h moderation cutoff | `LookupReview`, `SoftDeleteReview`, `RefreshFirstPageCache` |
| `RateReviewWorkflow` | None — votes are uncontentious | `UpsertVote` (UPSERT + score recompute in one tx), `RefreshFirstPageCache` |

There's no admin UI yet. Moderator approval happens by **opening the pending workflow in the Temporal UI** (<http://localhost:8233>) and sending a typed `Approve` or `Reject` signal. The workflow's `WaitConditionAsync` resumes from there — that's the whole moderation surface, deliberately kept thin so a real admin tool can replace it without changing the durable contract.

The cache invariant is workflow-owned: the `RefreshFirstPageCache` activity blows away the page-1 Redis key after every mutation, and the next read repopulates from Postgres. Crash between persist and cache-refresh? Temporal retries the failed activity, not the whole flow.

### Why a separate worker process

- **Workers are the unit of horizontal scale for Temporal.** You scale workers (CPU-bound work) independently from the API (request-bound work).
- **Workflow code redeploy semantics differ from API code.** Active workflows pin to the worker version that started them; rolling out workflow changes is a versioned operation. Coupling that to API deploys is painful.

### Why Angular SSR, not CSR or static

This is a reviews platform — product and review pages need to be crawlable for SEO with full content rendered server-side, including structured data. CSR would tank organic discovery. The same Express server doubles as the BFF, so SSR pass and OIDC plumbing share one Node process.

### Cache shape

Page-1 review listings (default sort=newest, no filters) get cached at `reviews:product:{id}:page:1` with a 1h TTL. The cached payload deliberately strips per-viewer fields (`MyVote`, `Mine`); on read, the API merges those in via a single PK lookup against `review_votes`. Other sorts/filters/pages go straight to Postgres — sort × filter × page would explode the cache cardinality, and the long tail isn't worth it.

## Verifying the run

After bringing the stack up:

1. Visit <http://localhost:4000>. The product list shows 10 items with star ratings.
2. Click a product (e.g. Sony WH-1000XM5 → mostly 4-5 star, or Acme Smartwatch → mostly 1-2 star) to see its first page of reviews. The Sony page averages ~4.6; Acme ~1.8 — the seed mix is what makes the rating UI obviously do something.
3. Click **Sign in** → ZITADEL login → enter `alice` / `Password1!` → redirected back signed in.
4. **Write a review**. 3- or 4-star reviews appear immediately. 1-, 2-, or 5-star reviews are durably submitted but pending — open Temporal UI, find the `SubmitReview` workflow, click **Send Signal**, pick `Approve`, and the review lands.
5. **Vote** on any review. The score updates within a tick (the workflow runs async and the cache invalidates on completion).
6. **Edit your own review**. Edits within the first hour apply immediately. Edits after that wait for an `Approve` signal in Temporal UI — same mechanism as new-review moderation.
7. **Delete your own review**. Same 1h policy.

If you want to nuke and re-bootstrap auth (e.g. if you wiped Postgres but the local secrets are stale): `docker compose down -v && rm -rf infra/zitadel/.secrets infra/zitadel/.app-secrets`.

## Deferred for later milestones

- A real moderator surface (today: send a signal in the Temporal UI). Either an admin SPA, or — likely — an MCP server mounted on the API project so a Claude or another agent can fan out moderation actions.
- Roles / permissions beyond "authenticated" (a "moderator" claim that gates a workflow's signal endpoints).
- Reconciliation job for the denormalized `score` column (it's already self-healing on every vote, but a periodic full-reconcile is cheap insurance).
- Direct-to-Azurite uploads for user review images (currently the seed uploads images; user submissions accept image URLs as text).
