import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatToolbar } from '@angular/material/toolbar';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Session } from './core/auth/session';
import type { NavItem } from './shell/nav-menu';
import { NavMenu } from './shell/nav-menu';

@Component({
  selector: 'app-root',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatToolbar,
    MatButton,
    MatIconButton,
    MatIcon,
    NavMenu,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly session = inject(Session);
  protected readonly menuRequested = signal(false);

  protected readonly navItems = computed<NavItem[]>(() =>
    this.session.isSignedIn()
      ? [
          { label: 'Find hockey', icon: 'search', link: '/', exact: true },
          { label: 'Your events', icon: 'event', link: '/host', exact: true },
          { label: 'Account', icon: 'account_circle', link: '/host/account', exact: false },
          ...(this.session.isAdmin()
            ? [{ label: 'Admin', icon: 'admin_panel_settings', link: '/admin', exact: false }]
            : []),
        ]
      : [
          { label: 'Find hockey', icon: 'search', link: '/', exact: true },
          { label: 'For hosts', icon: 'sports_hockey', link: '/host', exact: false },
        ],
  );
}
