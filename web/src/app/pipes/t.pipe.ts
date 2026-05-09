import { Pipe, PipeTransform, inject } from '@angular/core';
import { I18nService } from '../services/i18n.service';

// Templates use `{{ 'common.submit' | t }}` for keys without params, or
// `{{ 'common.waitingUploads' | t: { n: count() } }}` for placeholders.
//
// `pure: false` so the pipe re-runs whenever change detection ticks — needed
// because the underlying I18nService.t() reads a signal (locale()) that
// Angular's pure-pipe contract doesn't track. The cost is negligible (a JSON
// lookup) and the behaviour matches what users expect: switching language
// updates every visible string immediately.
@Pipe({ name: 't', pure: false })
export class TPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(key: string, params?: Record<string, string | number>): string {
    return this.i18n.t(key, params);
  }
}
