import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatIcon } from '@angular/material/icon';
import { RouterLink } from '@angular/router';

/** Quiet text link to the skill levels guide. `newTab` keeps the current page (and its form state) open. */
@Component({
  selector: 'app-skill-levels-link',
  imports: [RouterLink, MatIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (newTab()) {
      <a class="link" routerLink="/skill-levels" target="_blank" rel="noopener">
        What do levels mean?<span class="visually-hidden"> (opens in a new tab)</span>
        <mat-icon class="icon" aria-hidden="true">open_in_new</mat-icon>
      </a>
    } @else {
      <a class="link" routerLink="/skill-levels">What do levels mean?</a>
    }
  `,
  styles: `
    :host {
      display: inline-block;
    }

    .link {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      min-height: 24px;
      font: var(--mat-sys-label-large);
      letter-spacing: var(--mat-sys-label-large-tracking);
      color: var(--mat-sys-primary);
      border-radius: var(--mat-sys-corner-extra-small);
    }

    .icon {
      width: 16px;
      height: 16px;
      font-size: 16px;
    }
  `,
})
export class SkillLevelsLink {
  readonly newTab = input(false);
}
