import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatChip, MatChipAvatar, MatChipSet } from '@angular/material/chips';
import { MatIcon } from '@angular/material/icon';

export type StatusChipTone = 'neutral' | 'positive' | 'critical';

/**
 * Non-interactive status badge built on `mat-chip` (for example CONFIRMED, DRAFT, CANCELLED).
 * The label is the accessible text; the icon is decorative.
 */
@Component({
  selector: 'app-status-chip',
  imports: [MatChipSet, MatChip, MatChipAvatar, MatIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class]': "'tone-' + tone()" },
  template: `
    <mat-chip-set>
      <mat-chip [disableRipple]="true" [highlighted]="tone() !== 'neutral'">
        @if (icon(); as name) {
          <mat-icon matChipAvatar aria-hidden="true">{{ name }}</mat-icon>
        }
        {{ label() }}
      </mat-chip>
    </mat-chip-set>
  `,
  styles: `
    :host {
      display: inline-block;
      vertical-align: middle;
    }

    :host(.tone-positive) {
      --mat-chip-elevated-selected-container-color: var(--mat-sys-primary-container);
      --mat-chip-selected-label-text-color: var(--mat-sys-on-primary-container);
      --mat-chip-with-icon-selected-icon-color: var(--mat-sys-on-primary-container);
    }

    :host(.tone-critical) {
      --mat-chip-elevated-selected-container-color: var(--mat-sys-error-container);
      --mat-chip-selected-label-text-color: var(--mat-sys-on-error-container);
      --mat-chip-with-icon-selected-icon-color: var(--mat-sys-on-error-container);
    }
  `,
})
export class StatusChip {
  readonly label = input.required<string>();
  readonly tone = input<StatusChipTone>('neutral');
  readonly icon = input<string>();
}
