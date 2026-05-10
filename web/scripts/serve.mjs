#!/usr/bin/env node
// Launcher that forwards $PORT into `ng serve --port <port>`. Angular's dev
// server doesn't read PORT itself, so without this Aspire's randomized
// PORT injection gets ignored and ng quietly binds to its 4200 default —
// the dashboard then points at a port nothing's listening on while ng
// serves on an orphaned 4200. Falls back to 4200 when PORT isn't set so
// `npm start` standalone still works.
//
// Cross-platform via `shell: true` so npx is found on Windows cmd too.
import { spawn } from 'node:child_process';

const port = process.env.PORT ?? '4200';
const forwarded = process.argv.slice(2);
const args = ['ng', 'serve', '--port', port, ...forwarded];

const child = spawn('npx', args, { stdio: 'inherit', shell: true });
child.on('exit', (code) => process.exit(code ?? 0));
