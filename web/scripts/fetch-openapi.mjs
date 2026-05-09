#!/usr/bin/env node
// Pull the OpenAPI spec from a running API into src/api/openapi.json so the
// codegen step has a fresh source. Default URL points at the Aspire-hosted
// API; override with OPENAPI_URL when running against compose / a deployed
// environment.
//
// Workflow:
//   1. Start the backend (`dotnet run --project backend/api` or via apphost)
//   2. `npm run fetch:openapi`
//   3. `npm run generate:client`
//
// Steps 2 + 3 are split so a checked-in spec can be regenerated to TS without
// needing the backend running.

import { writeFile, mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const url = process.env.OPENAPI_URL ?? 'http://localhost:5180/swagger/v1/swagger.json';
const __dirname = dirname(fileURLToPath(import.meta.url));
const outPath = join(__dirname, '..', 'src', 'api', 'openapi.json');

async function main() {
    const res = await fetch(url);
    if (!res.ok) {
        throw new Error(`Fetching ${url} failed: ${res.status} ${res.statusText}`);
    }
    const spec = await res.json();
    await mkdir(dirname(outPath), { recursive: true });
    await writeFile(outPath, JSON.stringify(spec, null, 2) + '\n', 'utf8');
    console.log(`Wrote ${outPath}`);
}

main().catch((err) => {
    console.error(err);
    process.exit(1);
});
