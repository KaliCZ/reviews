import { RenderMode, ServerRoute } from '@angular/ssr';

export const serverRoutes: ServerRoute[] = [
  // SSR per request — product pages and review listings need fresh data and
  // current-user-aware enrichment (my-vote, my-existing-review-id), so
  // prerender doesn't work. The first visit's render is what crawlers see;
  // hydration takes over for the user.
  {
    path: '**',
    renderMode: RenderMode.Server,
  },
];
