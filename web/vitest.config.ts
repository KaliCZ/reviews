import { defineConfig } from 'vitest/config';

// Two projects so node tests don't try to load Angular component code:
//   - bff (node) — Express/BFF modules
//   - spa-services (jsdom) — SPA services that need localStorage / matchMedia
//     but not TestBed.
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
