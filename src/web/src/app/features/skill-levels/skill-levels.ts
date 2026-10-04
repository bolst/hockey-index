import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { PageHeader } from '../../shared/page-header/page-header';
import { SKILL_LEVELS } from './skill-levels.data';

@Component({
  selector: 'app-skill-levels',
  imports: [RouterLink, MatButton, MatIcon, PageHeader],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'app-page' },
  template: `
    <app-page-header
      headline="Skill levels"
      supportingText="Use these levels to find games that match how you play. Hosts set a level range for each event."
    />

    <aside class="note" aria-labelledby="half-levels-heading">
      <mat-icon class="note-icon" aria-hidden="true">info</mat-icon>
      <div>
        <h2 id="half-levels-heading" class="note-title">Half levels</h2>
        <p class="note-text">
          Events list whole levels only. If you are a 2.5, look for events whose range includes 2 or 3. The same goes
          for 3.5.
        </p>
      </div>
    </aside>

    <ol class="levels" aria-label="Skill levels from 0 to 7">
      @for (item of levels; track item.level) {
        <li class="level" data-testid="skill-level">
          <span class="number" aria-hidden="true">{{ item.level }}</span>
          <div class="body">
            <h2 class="level-title">Level {{ item.level }}</h2>
            <p class="description">{{ item.description }}</p>
            @if (item.leagues.length) {
              <div class="leagues" data-testid="skill-leagues">
                <span class="leagues-label" [id]="'leagues-' + item.level">Equivalent leagues</span>
                <ul class="league-list" [attr.aria-labelledby]="'leagues-' + item.level">
                  @for (league of item.leagues; track league) {
                    <li class="league">{{ league }}</li>
                  }
                </ul>
              </div>
            }
          </div>
        </li>
      }
    </ol>

    <div class="app-actions end">
      <a matButton="filled" routerLink="/">
        <mat-icon aria-hidden="true">search</mat-icon>
        Find events
      </a>
    </div>
  `,
  styles: `
    .note {
      display: flex;
      gap: 12px;
      margin-bottom: 24px;
      padding: 16px;
      border-radius: var(--mat-sys-corner-large);
      background: var(--mat-sys-secondary-container);
      color: var(--mat-sys-on-secondary-container);
    }

    .note-icon {
      flex: none;
    }

    .note-title {
      margin: 0 0 4px;
      font: var(--mat-sys-title-small);
      letter-spacing: var(--mat-sys-title-small-tracking);
    }

    .note-text {
      margin: 0;
      font: var(--mat-sys-body-medium);
    }

    .levels {
      display: flex;
      flex-direction: column;
      gap: 12px;
      margin: 0 0 24px;
      padding: 0;
      list-style: none;
    }

    .level {
      display: flex;
      gap: 16px;
      padding: 16px;
      border-radius: var(--mat-sys-corner-large);
      background: var(--mat-sys-surface-container);
    }

    .number {
      flex: none;
      display: grid;
      place-items: center;
      width: 64px;
      height: 64px;
      border-radius: var(--mat-sys-corner-large);
      background: var(--mat-sys-primary-container);
      color: var(--mat-sys-on-primary-container);
      font: var(--mat-sys-headline-medium);
      letter-spacing: var(--mat-sys-headline-medium-tracking);
    }

    .body {
      display: flex;
      flex-direction: column;
      gap: 4px;
      min-width: 0;
    }

    .level-title {
      margin: 0;
      font: var(--mat-sys-title-medium);
      letter-spacing: var(--mat-sys-title-medium-tracking);
      color: var(--mat-sys-on-surface);
    }

    .description {
      margin: 0;
      font: var(--mat-sys-body-medium);
      color: var(--mat-sys-on-surface-variant);
    }

    .leagues {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 8px 12px;
      margin-top: 8px;
    }

    .league-list {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .leagues-label {
      font: var(--mat-sys-label-medium);
      color: var(--mat-sys-on-surface-variant);
    }

    .league {
      padding: 4px 12px;
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: var(--mat-sys-corner-small);
      font: var(--mat-sys-label-large);
      color: var(--mat-sys-on-surface-variant);
      background: var(--mat-sys-surface-container-low);
    }

    .end {
      justify-content: flex-end;
    }

    @media (max-width: 599.98px) {
      .level {
        gap: 12px;
        padding: 12px;
      }

      .number {
        width: 48px;
        height: 48px;
        font: var(--mat-sys-headline-small);
      }
    }
  `,
})
export class SkillLevels {
  protected readonly levels = SKILL_LEVELS;
}
