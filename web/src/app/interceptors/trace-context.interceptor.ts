import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { TraceContextService } from '../services/trace-context.service';

// Stamps every same-origin request (which is all of them — the BFF proxies
// /api/* through itself) with a W3C traceparent so the BFF's HTTP server
// span continues the browser's trace_id instead of starting a new one.
export const traceContextInterceptor: HttpInterceptorFn = (req, next) => {
  const ctx = inject(TraceContextService);
  return next(req.clone({ setHeaders: { traceparent: ctx.traceparent() } }));
};
