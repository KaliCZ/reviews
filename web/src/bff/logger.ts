import { logs, SeverityNumber } from '@opentelemetry/api-logs';
import { trace } from '@opentelemetry/api';

// Pino would be the obvious choice, but Angular's esbuild bundles it into the
// SSR output, so the OTel pino instrumentation can't patch it and the bridge
// to OTLP silently drops every record. Calling the logs API directly works
// regardless of bundling — the LoggerProvider is registered globally by
// otel.register.mjs and accessed via the API package, which is small and
// import-stable.

const otelLogger = logs.getLogger(process.env['OTEL_SERVICE_NAME'] ?? 'web');

type Fields = Record<string, unknown>;

function emit(severity: SeverityNumber, severityText: string, a: unknown, b?: string): void {
  const obj = typeof a === 'string' ? null : (a as Fields | null | undefined);
  const msg = typeof a === 'string' ? a : (b ?? '');
  const attributes: Fields = { ...(obj ?? {}) };

  // Drop Error instances to a serializable shape; pino-style { err } convention.
  if (attributes['err'] instanceof Error) {
    const e = attributes['err'] as Error;
    attributes['err'] = { name: e.name, message: e.message, stack: e.stack };
  }

  // Local stdout copy keeps the Aspire console feed and `npm run dev` readable
  // when the OTLP exporter isn't wired up.
  const line = JSON.stringify({ level: severityText.toLowerCase(), msg, ...attributes });
  if (severity >= SeverityNumber.ERROR) process.stderr.write(line + '\n');
  else process.stdout.write(line + '\n');

  const ctx = trace.getActiveSpan()?.spanContext();
  otelLogger.emit({
    severityNumber: severity,
    severityText,
    body: msg,
    attributes: attributes as Record<string, string | number | boolean>,
    // The pino instrumentation would inject these; we do it ourselves.
    ...(ctx ? { traceId: ctx.traceId, spanId: ctx.spanId, traceFlags: ctx.traceFlags } : {}),
  });
}

export const logger = {
  info: (a: unknown, b?: string) => emit(SeverityNumber.INFO, 'INFO', a, b),
  warn: (a: unknown, b?: string) => emit(SeverityNumber.WARN, 'WARN', a, b),
  error: (a: unknown, b?: string) => emit(SeverityNumber.ERROR, 'ERROR', a, b),
};
