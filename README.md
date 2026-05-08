# Reviews

A product reviews platform. This kickoff seeds the monorepo and a hello-world endpoint that proves the wiring works end-to-end (Angular → API → Redis).

## Stack

- **API** — ASP.NET Core 10 (`api/`)
- **Frontend** — Angular 21 with SSR (`web/`)
- **Cache** — Redis
- **Database** — PostgreSQL (one cluster, separate databases for app + auth)
- **Auth** — ZITADEL (OIDC, runs locally as a container; not yet wired into code)
- **Blob storage** — Azurite locally (Azure Storage emulator, real Azure Blob in production)
- **Orchestration** — .NET Aspire (`apphost/`) + Docker Compose

## Prerequisites

- .NET 10 SDK
- Node 24
- Docker Desktop

## Setup

```bash
cp .env.example .env
npm install
npm --prefix web install
```

## Three ways to run

### 1. Aspire (richest dev experience)

```bash
npm run aspire
# or: dotnet run --project apphost
```

Spins up all services with the Aspire dashboard at the URL printed on startup. You get a unified log/trace/metrics view, hot reload on the API and Angular, and Aspire injects connection strings into the API automatically.

No extra install needed beyond the prerequisites — the Aspire NuGet packages come down via `dotnet restore` like any dependency.

### 2. `npm run dev` (no Aspire workload required)

```bash
npm run dev
```

Starts Postgres, Redis, Azurite, and ZITADEL via `docker compose -d`, then runs `dotnet watch` and `ng serve` on the host with hot reload. This is the lightest dev loop — just Docker for the infra, native dev servers for the code.

Stop the infra containers with `npm run dev:infra:down`.

### 3. Docker Compose (zero-install demo path)

```bash
npm run compose:up
```

Builds and runs everything containerized. Reviewer needs only Docker. Slower iteration (rebuilds on change) but faithfully matches what gets deployed.

After it boots:
- Frontend: <http://localhost:4000>
- API: <http://localhost:8081>
- ZITADEL Console: <http://localhost:8080>

## Project structure

```
reviews/
├── api/                    .NET API (ASP.NET Core, Minimal API)
├── apphost/                Aspire orchestration project
├── service-defaults/       Shared OTel / health-check / service-discovery wiring
├── web/                    Angular SSR frontend
├── infra/                  Infra helpers (Postgres init script)
├── docker-compose.yml      Containerized run path
├── package.json            Root scripts: dev, aspire, compose:up
└── .env.example            Local secrets template (copy to .env)
```

## Design notes

### Why three run paths?

- **Aspire** for the inner dev loop — it gives the best observability and least friction once installed.
- **`npm run dev`** as a fallback if you don't want the Aspire workload. Same hot-reload story, just orchestrated by `concurrently` + `docker compose` instead.
- **Docker Compose** so a reviewer can clone and run with only Docker installed. Also matches deployment topology.

The three paths share the same code; they only differ in how connection strings are injected.

### Secrets and configuration

Connection strings flow through .NET's `IConfiguration`. The source varies per environment, the code doesn't:

- **Aspire** — Aspire injects connection strings as env vars via `WithReference()`. Secrets come from `apphost/appsettings.Development.json` `Parameters` section (or user secrets).
- **Docker Compose** — The repo-level `.env` file is the single source of truth, fanned out via `${VAR}` substitution in `docker-compose.yml`.
- **Production** — Add `builder.Configuration.AddAzureKeyVault(...)` in `api/Program.cs` gated on `!IsDevelopment()`. Same code reads the values, source changes.

A local Vault container (HashiCorp Vault dev mode) was considered and deferred as YAGNI for the dev loop. The plumbing is set up such that adding it later is a configuration-source swap, not a code change.

### Why Angular SSR, not CSR or static?

This is a reviews platform — product and review pages need to be crawlable for SEO with full content rendered server-side, including structured data (`schema.org/Review`). CSR would tank organic discovery. Long-term the plan is hybrid: SSR for product/review pages, prerender for marketing, CSR for the user dashboard.

### Why is the Angular app the entry point in compose, not nginx?

With SSR, the web container *is* a Node Express server (`dist/web/server/server.mjs`). It both renders the SPA server-side and reverse-proxies `/api/*` to the API container — meaning the browser only ever talks to one origin. This eliminates CORS in the prod path and matches what a real deployment behind a single domain would look like.

In dev, Angular's dev server proxy (`web/proxy.conf.js`) does the same `/api/*` forwarding, reading `API_URL` from the env Aspire injects.

### Auth flow: BFF pattern (planned)

When ZITADEL is wired in, the Angular SSR Express server doubles as a Backend-For-Frontend:

- The browser holds an HTTP-only session cookie. **No tokens in JavaScript.**
- The Express server runs the OIDC code flow against ZITADEL (`/auth/login`, `/auth/callback`, `/auth/logout`).
- Tokens are kept server-side, keyed by session. Single-instance: in memory. Real deploys: Redis-backed (we already have Redis in the stack).
- The existing `/api/*` proxy gets a middleware that reads the session and attaches `Authorization: Bearer …` to the upstream API call.
- The .NET API treats those Bearer tokens as a standard OIDC resource server (JWT validation against ZITADEL's JWKS).

This is the OWASP-recommended pattern for SPAs in 2025 and lets the SSR pass also know who the user is on first render.

### Why ZITADEL over Keycloak / GoTrue / FusionAuth / SuperTokens?

- **ZITADEL** is modern, OIDC-spec-compliant, lightweight (Go, ~150MB image, ~5s startup), and supports the auth bits an MCP server with full OAuth 2.1 would need (Dynamic Client Registration, Resource Indicators).
- **Keycloak** has a first-party Aspire integration but is Java (~600MB, ~30s startup).
- **GoTrue** is anemic standalone (built for Supabase context).
- **FusionAuth** is Java-weight without Keycloak's Aspire integration.
- **SuperTokens** is more SDK-than-server-shaped.

The ZITADEL container is seeded but not yet wired into the API or frontend. Auth is the next milestone.

### Why Azurite for blob storage?

It's the Microsoft-official local emulator for Azure Storage. Aspire's storage integration has a one-line `RunAsEmulator()` toggle that runs the Azurite container locally; the same code talks to real Azure Blob in production with just a different connection string. If we'd picked S3 / MinIO instead, the same pattern would work, but Azurite pairs more naturally with the Aspire → Azure Container Apps deployment story.

### Deferred for later milestones

- Wire ZITADEL into the API as an OIDC resource server, and into Angular as the OIDC client (with `/api/config` for SPA bootstrap config so we keep a single browser bundle across environments).
- Domain model: products, reviews, ratings, moderation.
- The MCP server, mounted on the API project (no separate host).

## Verifying the kickoff

After running any of the three paths, visit the frontend URL, click **Say hello**, and the page should show `Hello from the API — count: N` with `N` incrementing on each click. That round-trip exercises Angular → SSR proxy → API → Redis.
