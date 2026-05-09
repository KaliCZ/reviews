import { RenderMode, ServerRoute } from '@angular/ssr';

export const serverRoutes: ServerRoute[] = [
  // Per-request SSR (not prerender): pages need fresh data and viewer-aware
  // enrichment (myVote, myReviewId).
  {
    path: '**',
    renderMode: RenderMode.Server,
  },
];
