import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIcon } from '@angular/material/icon';

export type InlineMessageKind = 'error' | 'info' | 'success';

const ICONS: Record<InlineMessageKind, string> = {
  error: 'error',
  info: 'info',
  success: 'check_circle',
};

/**
 * Persistent message tied to a form or section (API errors, quota notices). Errors use
 * `role="alert"`; other kinds use `role="status"`. Transient confirmations belong in a snack bar
 * (see Notifier), and field validation belongs in `<mat-error>`.
 */
@Component({
  selector: 'app-inline-message',
  imports: [MatIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[attr.role]': "kind() === 'error' ? 'alert' : 'status'",
    '[class]': "'kind-' + kind()",
  },
  template: `
    <mat-icon aria-hidden="true">{{ icon() }}</mat-icon>
    <span class="text"><ng-content /></span>
  `,
  styles: `
    :host {
      display: flex;
      align-items: flex-start;
      gap: 12px;
      padding: 12px 16px;
      border-radius: var(--mat-sys-corner-medium);
      font: var(--mat-sys-body-medium);
    }

    .text {
      padding-top: 2px;
    }

    :host(.kind-error) {
      background: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
    }

    :host(.kind-info) {
      background: var(--mat-sys-secondary-container);
      color: var(--mat-sys-on-secondary-container);
    }

    :host(.kind-success) {
      background: var(--mat-sys-tertiary-container);
      color: var(--mat-sys-on-tertiary-container);
    }
  `,
})
export class InlineMessage {
  readonly kind = input<InlineMessageKind>('info');
  protected readonly icon = computed(() => ICONS[this.kind()]);
}
