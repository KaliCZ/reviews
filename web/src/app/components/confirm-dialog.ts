import { Component, ElementRef, Injectable, ViewChild, inject, signal } from '@angular/core';

interface ConfirmRequest {
  message: string;
  confirmLabel: string;
  cancelLabel: string;
  destructive: boolean;
  resolve: (ok: boolean) => void;
}

// Service-driven imperative confirm — drop-in replacement for window.confirm()
// that returns a Promise<boolean>. Keeps the page interactive (no JS-thread
// stall) and lets the message + buttons pick up i18n + brand styling.
@Injectable({ providedIn: 'root' })
export class ConfirmDialogService {
  readonly current = signal<ConfirmRequest | null>(null);

  show(opts: {
    message: string;
    confirmLabel?: string;
    cancelLabel?: string;
    destructive?: boolean;
  }): Promise<boolean> {
    return new Promise((resolve) => {
      this.current.set({
        message: opts.message,
        confirmLabel: opts.confirmLabel ?? 'OK',
        cancelLabel: opts.cancelLabel ?? 'Cancel',
        destructive: opts.destructive ?? false,
        resolve,
      });
    });
  }

  resolve(ok: boolean) {
    const req = this.current();
    if (!req) return;
    this.current.set(null);
    req.resolve(ok);
  }
}

@Component({
  selector: 'app-confirm-dialog',
  template: `
    @if (svc.current(); as req) {
      <dialog #dlg class="confirm" (close)="svc.resolve(false)">
        <p class="message">{{ req.message }}</p>
        <div class="actions">
          <button type="button" class="btn cancel" (click)="svc.resolve(false)">
            {{ req.cancelLabel }}
          </button>
          <button
            type="button"
            class="btn confirm-btn"
            [class.destructive]="req.destructive"
            (click)="svc.resolve(true)"
            autofocus
          >
            {{ req.confirmLabel }}
          </button>
        </div>
      </dialog>
    }
  `,
  styles: [
    `
      .confirm {
        border: 1px solid var(--color-outline);
        border-radius: 8px;
        padding: 1.25rem 1.5rem;
        max-width: 28rem;
        background: var(--color-surface, #fff);
        color: var(--color-on-surface, inherit);
        box-shadow: 0 10px 30px rgba(0, 0, 0, 0.2);
      }
      .confirm::backdrop {
        background: rgba(0, 0, 0, 0.4);
      }
      .message {
        margin: 0 0 1rem;
        line-height: 1.4;
      }
      .actions {
        display: flex;
        gap: 0.5rem;
        justify-content: flex-end;
      }
      .btn {
        padding: 0.45rem 0.9rem;
        border-radius: 4px;
        border: 1px solid var(--color-outline);
        background: var(--color-surface, #fff);
        color: var(--color-on-surface, inherit);
        cursor: pointer;
        font-size: 0.9rem;
      }
      .btn:hover {
        background: var(--color-surface-container, #f3f3f3);
      }
      .btn.confirm-btn {
        background: #2563eb;
        color: #fff;
        border-color: #2563eb;
      }
      .btn.confirm-btn:hover {
        background: #1d4ed8;
      }
      .btn.confirm-btn.destructive {
        background: #dc2626;
        border-color: #dc2626;
      }
      .btn.confirm-btn.destructive:hover {
        background: #b91c1c;
      }
    `,
  ],
})
export class ConfirmDialog {
  protected readonly svc = inject(ConfirmDialogService);
  @ViewChild('dlg') dlg?: ElementRef<HTMLDialogElement>;

  ngAfterViewChecked() {
    const el = this.dlg?.nativeElement;
    if (el && this.svc.current() && !el.open) el.showModal();
  }
}
