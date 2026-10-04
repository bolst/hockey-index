import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatTabLink, MatTabNav, MatTabNavPanel } from '@angular/material/tabs';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { PageHeader } from '../../shared/page-header/page-header';

interface AdminTab {
  path: string;
  label: string;
}

@Component({
  selector: 'app-admin-shell',
  imports: [PageHeader, MatTabNav, MatTabLink, MatTabNavPanel, RouterLink, RouterLinkActive, RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'app-page app-page--wide' },
  template: `
    <app-page-header headline="Admin" />
    <nav mat-tab-nav-bar [tabPanel]="panel" aria-label="Admin sections">
      @for (tab of tabs; track tab.path) {
        <a mat-tab-link [routerLink]="tab.path" routerLinkActive #rla="routerLinkActive" [active]="rla.isActive">
          {{ tab.label }}
        </a>
      }
    </nav>
    <mat-tab-nav-panel #panel class="panel">
      <router-outlet />
    </mat-tab-nav-panel>
  `,
  styles: `
    nav {
      max-width: 100%;
    }
    .panel {
      display: block;
      padding-top: 24px;
    }
  `,
})
export class AdminShell {
  protected readonly tabs: AdminTab[] = [
    { path: 'queue', label: 'Queue' },
    { path: 'hosts', label: 'Hosts' },
    { path: 'venues', label: 'Venues' },
    { path: 'blocklist', label: 'Blocklist' },
    { path: 'invites', label: 'Invites' },
    { path: 'breakers', label: 'Breakers' },
    { path: 'audit', label: 'Audit' },
  ];
}
