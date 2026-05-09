import pino from 'pino';

// Pino's instrumentation (loaded by otel.register.mjs) wraps this constructor
// and bridges every record into the OTel log pipeline, attaching the active
// trace_id / span_id automatically. Locally — when no OTLP endpoint is wired
// up — pino just prints JSON to stdout, so the same call sites work either way.
export const logger = pino({
  level: process.env['LOG_LEVEL'] ?? 'info',
  base: { service: process.env['OTEL_SERVICE_NAME'] ?? 'web' },
});
