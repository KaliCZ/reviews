#!/usr/bin/env node
// Wrapper around `dotnet run --project backend/apphost` that picks per-worktree
// host ports for the AppHost's own listeners (dashboard frontend, OTLP, MCP,
// resource service). launchSettings.json hardcodes those, so two parallel
// `npm run aspire` runs would otherwise fight over the same ports — the
// worktree-id machinery in AppHost.cs only covers Aspire-managed resources.
//
// Mirrors the worktreeId derivation in AppHost.cs (sha256 of cwd, first 4 hex
// chars mod 1000) so a given worktree gets stable URLs across restarts and
// different worktrees never collide. --no-launch-profile bypasses the pinned
// values in launchSettings entirely; we re-supply the env it would have set.
import { createHash } from 'node:crypto';
import { spawn } from 'node:child_process';

const worktreeId = createHash('sha256').update(process.cwd()).digest('hex').slice(0, 8);
const offset = parseInt(worktreeId.slice(0, 4), 16) % 1000;

const dashboardPort = 24000 + offset;
const otlpPort = 25000 + offset;
const mcpPort = 26000 + offset;
const resourceServicePort = 27000 + offset;

const child = spawn(
  'dotnet',
  ['run', '--project', 'backend/apphost', '--no-launch-profile'],
  {
    stdio: 'inherit',
    shell: process.platform === 'win32',
    env: {
      ...process.env,
      ASPNETCORE_URLS: `https://localhost:${dashboardPort}`,
      ASPNETCORE_ENVIRONMENT: 'Development',
      DOTNET_ENVIRONMENT: 'Development',
      ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL: `https://localhost:${otlpPort}`,
      ASPIRE_DASHBOARD_MCP_ENDPOINT_URL: `https://localhost:${mcpPort}`,
      ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL: `https://localhost:${resourceServicePort}`,
    },
  },
);

child.on('exit', (code, signal) => {
  if (signal) process.kill(process.pid, signal);
  else process.exit(code ?? 0);
});
