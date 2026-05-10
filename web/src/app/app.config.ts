import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import {
  provideClientHydration,
  withEventReplay,
  withHttpTransferCacheOptions,
} from '@angular/platform-browser';
import { provideHttpClient, withFetch } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Transfer cache disabled: SSR fetches don't carry the session cookie, so
    // cached responses are anonymous and clobber per-viewer fields (MyVote,
    // Mine) on hydration. The API owns the caching layer; the client re-fetches.
    provideClientHydration(
      withEventReplay(),
      withHttpTransferCacheOptions({ filter: () => false }),
    ),
    provideHttpClient(withFetch()),
    provideRouter(routes, withComponentInputBinding()),
  ],
};
