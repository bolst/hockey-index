import { ChangeDetectionStrategy, Component, afterNextRender, input, viewChild } from '@angular/core';
import { MatIconButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { RouterLink, RouterLinkActive } from '@angular/router';

export interface NavItem {
  label: string;
  icon: string;
  link: string;
  exact: boolean;
}

/**
 * The narrow-screen navigation menu. The shell loads it with `@defer` so the overlay and menu code
 * stay out of the initial bundle.
 */
@Component({
  selector: 'app-nav-menu',
  imports: [MatIconButton, MatIcon, MatMenu, MatMenuItem, MatMenuTrigger, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button matIconButton type="button" aria-label="Open navigation menu" [matMenuTriggerFor]="navMenu">
      <mat-icon aria-hidden="true">menu</mat-icon>
    </button>
    <mat-menu #navMenu="matMenu" xPosition="before" aria-label="Main">
      @for (item of items(); track item.link) {
        <a
          mat-menu-item
          [routerLink]="item.link"
          routerLinkActive="active"
          ariaCurrentWhenActive="page"
          [routerLinkActiveOptions]="{ exact: item.exact }"
        >
          <mat-icon aria-hidden="true">{{ item.icon }}</mat-icon>
          <span>{{ item.label }}</span>
        </a>
      }
    </mat-menu>
  `,
  styles: `
    .mat-mdc-menu-item.active {
      background: var(--mat-sys-secondary-container);
      color: var(--mat-sys-on-secondary-container);
    }
  `,
})
export class NavMenu {
  readonly items = input.required<NavItem[]>();
  /** Set when the user pressed the placeholder button before this component loaded. */
  readonly openOnLoad = input(false);
  private readonly trigger = viewChild.required(MatMenuTrigger);

  constructor() {
    afterNextRender(() => {
      if (this.openOnLoad()) {
        this.trigger().openMenu();
      }
    });
  }
}
