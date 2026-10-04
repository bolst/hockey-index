import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIcon } from '@angular/material/icon';

/**
 * Material empty state: a tonal icon, a headline, supporting text (projected), and an optional
 * `[emptyStateActions]` slot. Use `headingLevel` 1 only when the empty state is the whole page.
 */
@Component({
  selector: 'app-empty-state',
  imports: [MatIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { role: 'region', '[attr.aria-label]': 'headline()' },
  template: `
    <div class="icon-badge" aria-hidden="true">
      <mat-icon>{{ icon() }}</mat-icon>
    </div>
    @if (headingLevel() === 1) {
      <h1 class="headline">{{ headline() }}</h1>
    } @else {
      <h2 class="headline">{{ headline() }}</h2>
    }
    <div class="supporting"><ng-content /></div>
    <div class="actions"><ng-content select="[emptyStateActions]" /></div>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 8px;
      padding: 48px 24px;
      text-align: center;
      border-radius: var(--mat-sys-corner-extra-large);
      background: var(--mat-sys-surface-container-low);
      color: var(--mat-sys-on-surface);
    }

    .icon-badge {
      display: grid;
      place-items: center;
      width: 96px;
      height: 96px;
      margin-bottom: 16px;
      border-radius: var(--mat-sys-corner-full);
      background: var(--mat-sys-primary-container);
      color: var(--mat-sys-on-primary-container);
    }

    .icon-badge .mat-icon {
      width: 48px;
      height: 48px;
      font-size: 48px;
    }

    .headline {
      margin: 0;
      font: var(--mat-sys-headline-small);
    }

    .supporting {
      max-width: 52ch;
      font: var(--mat-sys-body-large);
      color: var(--mat-sys-on-surface-variant);
    }

    .supporting ::ng-deep p {
      margin: 0;
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      justify-content: center;
      gap: 8px;
      margin-top: 16px;
    }

    .actions:empty {
      display: none;
    }
  `,
})
export class EmptyState {
  readonly icon = input.required<string>();
  readonly headline = input.required<string>();
  readonly headingLevel = input<1 | 2>(2);
}
