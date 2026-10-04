import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Page title block: one `<h1>` per page, optional supporting text, and an `[pageHeaderActions]`
 * slot for the page's top-level actions (buttons, menus).
 */
@Component({
  selector: 'app-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="text">
      <h1 class="headline">{{ headline() }}</h1>
      @if (supportingText(); as text) {
        <p class="supporting">{{ text }}</p>
      }
    </div>
    <div class="actions">
      <ng-content select="[pageHeaderActions]" />
    </div>
  `,
  styles: `
    :host {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      justify-content: space-between;
      gap: 16px;
      margin-block: 8px 24px;
    }

    .text {
      flex: 1 1 280px;
      min-width: 0;
    }

    .headline {
      margin: 0;
      font: var(--mat-sys-headline-medium);
      letter-spacing: var(--mat-sys-headline-medium-tracking);
      color: var(--mat-sys-on-surface);
    }

    .supporting {
      margin: 8px 0 0;
      font: var(--mat-sys-body-large);
      color: var(--mat-sys-on-surface-variant);
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }

    .actions:empty {
      display: none;
    }

    @media (max-width: 599.98px) {
      .headline {
        font: var(--mat-sys-headline-small);
      }
    }
  `,
})
export class PageHeader {
  readonly headline = input.required<string>();
  readonly supportingText = input<string>();
}
