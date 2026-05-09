// Telemetry bootstrap. Loaded via Node's --import flag (see package.json
// `start` / `serve:ssr:web`) so OTel's monkey-patching wires into Express,
// HTTP, Redis, etc. before our app code imports them. Without --import,
// ESM imports outrun the patching and auto-instrumentation captures nothing.
//
// Aspire's AddJavaScriptApp injects OTEL_EXPORTER_OTLP_ENDPOINT and friends;
// we no-op when that's missing so plain `node` runs and `ng build` (which
// imports server.ts statically) don't try to dial a non-existent collector.

import { NodeSDK } from '@opentelemetry/sdk-node';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-proto';
import { OTLPMetricExporter } from '@opentelemetry/exporter-metrics-otlp-proto';
import { OTLPLogExporter } from '@opentelemetry/exporter-logs-otlp-proto';
import { PeriodicExportingMetricReader } from '@opentelemetry/sdk-metrics';
import { BatchLogRecordProcessor } from '@opentelemetry/sdk-logs';
import { getNodeAutoInstrumentations } from '@opentelemetry/auto-instrumentations-node';
import { HostMetrics } from '@opentelemetry/host-metrics';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { ATTR_SERVICE_NAME } from '@opentelemetry/semantic-conventions';

if (process.env.OTEL_EXPORTER_OTLP_ENDPOINT) {
  const sdk = new NodeSDK({
    resource: resourceFromAttributes({
      [ATTR_SERVICE_NAME]: process.env.OTEL_SERVICE_NAME ?? 'web',
    }),
    traceExporter: new OTLPTraceExporter(),
    metricReader: new PeriodicExportingMetricReader({
      exporter: new OTLPMetricExporter(),
    }),
    logRecordProcessors: [new BatchLogRecordProcessor(new OTLPLogExporter())],
    instrumentations: getNodeAutoInstrumentations({
      // fs spans drown out everything else and aren't actionable here.
      '@opentelemetry/instrumentation-fs': { enabled: false },
      // Pino bridges its records into the OTel log pipeline + injects trace ids.
      '@opentelemetry/instrumentation-pino': { enabled: true },
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
}
