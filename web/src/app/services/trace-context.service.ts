import { Injectable, inject } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';

// Generates W3C trace context (RFC 9110 / W3C Trace Context) for outgoing
// HTTP requests so the BFF and API can stitch every /api call from one page
// load into a single trace. The browser doesn't run a full OTel SDK — there's
// no exporter, no recorded spans, just a trace_id rotated on each top-level
// navigation. Server-side spans treat that trace_id as a remote parent and
// the dashboard groups them automatically.
@Injectable({ providedIn: 'root' })
export class TraceContextService {
  private readonly router = inject(Router);
  private currentTraceId = newTraceId();

  constructor() {
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe(() => {
      this.currentTraceId = newTraceId();
    });
  }

  // The span_id changes per request; the trace_id stays stable for the
  // lifetime of the current navigation. Sampled flag (01) keeps the trace
  // recorded server-side.
  traceparent(): string {
    return `00-${this.currentTraceId}-${newSpanId()}-01`;
  }
}

function newTraceId(): string {
  return randomHex(16);
}

function newSpanId(): string {
  return randomHex(8);
}

function randomHex(bytes: number): string {
  // Browser: crypto.getRandomValues. Node 19+ exposes it on globalThis.crypto
  // too, so SSR rendering goes down the same path.
  const buf = new Uint8Array(bytes);
  globalThis.crypto.getRandomValues(buf);
  let out = '';
  for (const b of buf) out += b.toString(16).padStart(2, '0');
  return out;
}
