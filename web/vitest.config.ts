import { defineConfig } from 'vitest/config';

// vitest covers the BFF (node) and the SPA services that don't need TestBed
// (jsdom for the localStorage / matchMedia mocks). Component tests still
// run via `ng test` and live alongside their components — this scope is
// intentionally narrow so node tests don't try to load Angular component
// code. The two suites use different environments, picked per-spec via
// vitest's projects feature.
export default defineConfig({
  test: {
    projects: [
      {
        test: {
          name: 'bff',
          include: ['src/bff/**/*.spec.ts'],
          environment: 'node',
        },
      },
      {
        test: {
          name: 'spa-services',
          include: ['src/app/services/**/*.spec.ts'],
          environment: 'jsdom',
        },
      },
    ],
  },
});
