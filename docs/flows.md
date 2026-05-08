# Product flows

The four core flows the product is built around. Each one names the components involved and the rationale for the path through them, so the wiring elsewhere in the repo (API endpoints, workflows, cache keys) can be read with the intent in view.

## 1. Viewing reviews (first page, cached)

When a user opens a product page, the SSR pass on the Angular server requests the first page of reviews for that product from the API. The API serves it from a Redis-cached payload keyed by product id. Cache miss falls through to postgres and rewrites the cache on the way back.

- **Why cache only this path:** the first page is what every visitor and every crawler sees, so it's by far the hottest read. Subsequent pages and filtered views are long-tail and don't justify cache cardinality.
- **Why server-side render it:** review content needs to be in the HTML for SEO. The reviews JSON-LD (`schema.org/Review`) on the product page lifts directly from the same payload.
- **Cache contents:** the page payload itself (already shaped for the UI), not raw rows. Avoids re-shaping on every hit.

## 2. Browsing more reviews (paginated, DB-backed)

Clicking *More reviews* takes the user to a paginated list view that supports sort (newest, most useful, highest, lowest) and filters (rating, verified, with photos). Each request hits the API, which queries postgres directly — no Redis on this path.

- **Why no cache:** the cross-product of sort × filter × page would explode the keyspace. The set of users who go past page one is also a small fraction of total traffic, so the absolute load is manageable from the DB.
- **Index strategy lives in postgres:** indexes on `(product_id, created_at)`, `(product_id, useful_count)`, etc. cover the sort orders we expose. No application-level pagination tricks beyond keyset pagination.

## 3. Submitting a review (Temporal workflow)

Submission is the most non-trivial flow because it crosses an asynchronous boundary (manual moderation can take days) and has to update both the database and the cache atomically from the user's perspective.

```
Browser → API → Temporal: SubmitReview workflow
                              │
                              ├─ rating ∈ {1, 2, 5}?
                              │     yes → ManualModerationActivity (waits on a signal)
                              │            │
                              │            └─ rejected → end
                              │            └─ approved → continue
                              │
                              ├─ PersistReviewActivity      (postgres insert)
                              │
                              └─ RefreshFirstPageCacheActivity   (Redis write)
```

- **Why the API stays in the path:** the frontend has exactly one server it talks to. Temporal's gRPC frontend isn't auth'd for end users; it would also let any client start any workflow. The API validates input, attaches the authenticated user, picks the workflow, and starts it.
- **Why Temporal and not just an async job queue:** the moderation branch is an arbitrary-length wait. Temporal's signal-and-resume model and durable timers give us the right primitives without rebuilding them in app code. Crashes between persist and cache-refresh are also handled — the workflow retries the activity that failed, not the whole thing.
- **Why these rating buckets for moderation:** 1-star and 2-star are the most common targets of competitor abuse and venting; 5-star is where most fake/incentivised reviews land. 3 and 4 are the boring middle and rarely worth the moderator's time. The split is a starting point — eventually we expect heuristics (account age, IP, repeat patterns) to replace or augment the bucket rule.
- **Why refresh the cache from inside the workflow:** the workflow already knows whether the review changed the first page (rating distribution, recency). Doing it here keeps the cache invariant tied to the durable execution, not to the API layer where a crash mid-write would silently desync.

## 4. Marking a review as useful

A user clicks *useful* on a review. The API records the vote and bumps the review's usefulness counter. If the affected review is on the first cached page (or its new score promotes it onto that page), the cache for that product gets refreshed.

- **Why through Temporal too (probably):** the same crash-safety argument as flow 3 — the postgres write and the cache refresh need to land together or be retried. Open question whether the volume justifies a full workflow per click vs. a lighter background task; we'll measure before deciding. For the kickoff, treating it the same as flow 3 keeps the moving parts uniform.
- **Idempotency:** votes are unique per `(user_id, review_id)`. Replays of the workflow don't double-count.
- **Cache-touch heuristic:** only refresh if the review is currently on the cached page or if its new score crosses the threshold of whatever's lowest on that page. Keeps unnecessary cache writes off the hot path.

## How this maps to the code today

The kickoff seed has the wiring (Angular → API → Temporal → Worker → Redis) proven end-to-end via the hello-world counter. None of the flows above are implemented yet — they're the next milestone. The skeleton each flow needs is already in place:

- API endpoints get added to `api/Program.cs`.
- Workflow types go in `shared/` (referenced by both API and worker).
- Activity implementations with their infra dependencies (postgres, Redis) go in `worker/`.
- Domain entities and migrations land in postgres via the `reviews` schema that `infra/postgres-init.sh` provisions.
