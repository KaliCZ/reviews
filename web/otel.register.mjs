// Telemetry bootstrap. Loaded via Node's --import flag (see package.json
// `start` / `serve:ssr:web`) so OTel's monkey-patching wires into Express,
// HTTP, Redis, etc. before our app code imports them. Without --import,
// ESM imports outrun the patching and auto-instrumentation captures nothing.
//
// Aspire's AddJavaScriptApp injects OTEL_EXPORTER_OTLP_ENDPOINT and friends;
// we no-op when that's missing so plain `node` runs and `ng build` (which
// imports server.ts statically) don't try to dial a non-existent collector.

import { NodeSDK } from '@opentelemetry/sdk-node';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-grpc';
import { OTLPMetricExporter } from '@opentelemetry/exporter-metrics-otlp-grpc';
import { OTLPLogExporter } from '@opentelemetry/exporter-logs-otlp-grpc';
import { PeriodicExportingMetricReader } from '@opentelemetry/sdk-metrics';
import { BatchLogRecordProcessor } from '@opentelemetry/sdk-logs';
import { getNodeAutoInstrumentations } from '@opentelemetry/auto-instrumentations-node';
import { HostMetrics } from '@opentelemetry/host-metrics';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { ATTR_SERVICE_NAME } from '@opentelemetry/semantic-conventions';
import { diag, DiagConsoleLogger, DiagLogLevel } from '@opentelemetry/api';
import { appendFileSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';

// Piscina spawns worker threads for Angular SSR; each runs --import again, so
// without this guard we'd register a second SDK in the same process and
// double-export every span.
const SDK_STARTED = Symbol.for('reviews.otel.sdk-started');
const alreadyStarted = globalThis[SDK_STARTED] === true;
globalThis[SDK_STARTED] = true;

// WIP — BFF OTLP exports currently fail with self-signed cert errors from
// @grpc/grpc-js because the dashboard's dev cert chain isn't trusted by the
// Node tls stack. This file-based log captures diag warnings/errors so the
// next debugging pass can see them; Aspire only routes BFF stdout/stderr to
// its dashboard panel, which is unhelpful when the panel itself is broken.
// Drop both this file log and the related --import diagnostics once the cert
// issue is resolved.
const statusPath = join(tmpdir(), 'reviews-otel-status.log');
const writeStatus = (level, msg) => {
  try {
    appendFileSync(statusPath, `${new Date().toISOString()} pid=${process.pid} ${level} ${msg}\n`);
  } catch {}
};
writeStatus('INFO', `loader fired; alreadyStarted=${alreadyStarted} endpoint=${process.env.OTEL_EXPORTER_OTLP_ENDPOINT ?? '<unset>'} extraCa=${process.env.NODE_EXTRA_CA_CERTS ?? '<unset>'}`);

class FileAndConsoleDiagLogger extends DiagConsoleLogger {
  warn(message, ...args) { writeStatus('WARN', `${message} ${args.map(a => typeof a === 'string' ? a : JSON.stringify(a)).join(' ')}`); super.warn(message, ...args); }
  error(message, ...args) { writeStatus('ERROR', `${message} ${args.map(a => typeof a === 'string' ? a : JSON.stringify(a)).join(' ')}`); super.error(message, ...args); }
}
diag.setLogger(new FileAndConsoleDiagLogger(), DiagLogLevel.WARN);

if (!alreadyStarted && process.env.OTEL_EXPORTER_OTLP_ENDPOINT) {
  const sdk = new NodeSDK({
    resource: resourceFromAttributes({
      [ATTR_SERVICE_NAME]: process.env.OTEL_SERVICE_NAME ?? 'web',
    }),
    traceExporter: new OTLPTraceExporter(),
    metricReaders: [
      new PeriodicExportingMetricReader({
        exporter: new OTLPMetricExporter(),
      }),
    ],
    logRecordProcessors: [new BatchLogRecordProcessor(new OTLPLogExporter())],
    instrumentations: getNodeAutoInstrumentations({
      // fs spans drown out everything else and aren't actionable here.
      '@opentelemetry/instrumentation-fs': { enabled: false },
      // SPA bundles fetch dozens of JS chunks / CSS / favicons per page
      // load; those create per-asset traces that bury the /api and /auth
      // calls we care about. Drop the HTTP server span for asset routes —
      // express middleware spans nested under them get orphaned and the
      // exporter discards orphans.
      '@opentelemetry/instrumentation-http': {
        ignoreIncomingRequestHook: (req) => {
          const url = req.url ?? '';
          return !url.startsWith('/api') && !url.startsWith('/auth');
        },
      },
      // We emit logs through @opentelemetry/api-logs ourselves (see
      // src/bff/logger.ts) because Angular's bundler eats pino, so the
      // instrumentation patch never applies.
      '@opentelemetry/instrumentation-pino': { enabled: false },
    }),
  });
  sdk.start();

  new HostMetrics({ name: process.env.OTEL_SERVICE_NAME ?? 'web' }).start();

  const shutdown = () => {
    sdk
      .shutdown()
      .catch(() => {})
      .finally(() => process.exit(0));
  };
  process.once('SIGTERM', shutdown);
  process.once('SIGINT', shutdown);
  writeStatus('INFO', 'NodeSDK started');
} else if (alreadyStarted) {
  writeStatus('INFO', 'SDK already started in this process; skipping');
} else {
  writeStatus('INFO', 'OTLP endpoint not set; SDK skipped');
}
