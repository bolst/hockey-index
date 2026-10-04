import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { switchMap } from 'rxjs';
import { AdminApi, AdminHost } from '../../../core/api/admin-api';
import { PhonePipe } from '../../../core/format/phone.pipe';
import { InlineMessage } from '../../../shared/inline-message/inline-message';
import { Notifier } from '../../../shared/notifier/notifier';
import { askReason } from '../../../shared/reason-dialog/reason-dialog';
import { StatusChip } from '../../../shared/status-chip/status-chip';
import { adminErrorMessage } from '../admin-errors';

const NOT_FOUND = 'No host with that ID.';

@Component({
  selector: 'app-admin-host-detail',
  imports: [DatePipe, RouterLink, MatButton, MatIcon, MatProgressBar, PhonePipe, InlineMessage, StatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'app-stack' },
  template: `
    <a routerLink="/admin/hosts" class="back">Find another host</a>
    <h2 class="section-title">Host</h2>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading host" />
    }
    @if (error(); as message) {
      <app-inline-message kind="error">{{ message }}</app-inline-message>
    }
    @if (host(); as h) {
      <dl class="facts">
        <dt>ID</dt>
        <dd>{{ h.id }}</dd>
        <dt>Status</dt>
        <dd>
          <app-status-chip [label]="h.status" [tone]="h.status === 'banned' ? 'critical' : h.status === 'active' ? 'positive' : 'neutral'" />
          @if (h.isAdmin) {
            <app-status-chip label="Admin" />
          }
        </dd>
        <dt>Phone</dt>
        <dd>{{ (h.phone | phone) || 'None' }}</dd>
        <dt>Email</dt>
        <dd>{{ h.email ?? 'None' }}</dd>
        <dt>Created</dt>
        <dd>{{ h.createdAt | date: 'medium' }}</dd>
        <dt>Last sign-in</dt>
        <dd>{{ h.lastLoginAt ? (h.lastLoginAt | date: 'medium') : 'Never' }}</dd>
        @if (h.bannedAt) {
          <dt>Banned</dt>
          <dd>{{ h.bannedAt | date: 'medium' }}</dd>
        }
      </dl>
      <div class="app-actions">
        @if (h.status === 'banned') {
          <button matButton="outlined" type="button" (click)="unban(h)">
            <mat-icon aria-hidden="true">lock_open</mat-icon>
            Unban
          </button>
        } @else {
          <button matButton="filled" type="button" class="destructive" (click)="ban(h)">
            <mat-icon aria-hidden="true">block</mat-icon>
            Ban
          </button>
        }
      </div>
      <h3 class="subhead">Events ({{ h.events.length }})</h3>
      @if (h.events.length) {
        <ul class="events">
          @for (event of h.events; track event.publicId) {
            <li>
              <a [routerLink]="['/e', event.publicId]">{{ event.title }}</a>
              <app-status-chip [label]="event.status" [tone]="event.status === 'hidden' ? 'critical' : event.status === 'published' ? 'positive' : 'neutral'" />
              <span class="when">{{ event.startsAt | date: 'medium' }}</span>
            </li>
          }
        </ul>
      } @else {
        <p>No events.</p>
      }
    }
  `,
  styles: `
    .section-title {
      margin: 0;
      font: var(--mat-sys-title-large);
    }
    .subhead {
      margin: 8px 0 0;
      font: var(--mat-sys-title-medium);
    }
    .facts {
      display: grid;
      grid-template-columns: max-content minmax(0, 1fr);
      gap: 8px 16px;
      margin: 0;
    }
    dt {
      color: var(--mat-sys-on-surface-variant);
    }
    dd {
      margin: 0;
      overflow-wrap: anywhere;
    }
    .events {
      margin: 0;
      padding-left: 20px;
    }
    .events li {
      margin-bottom: 8px;
    }
    .when {
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
    .destructive {
      --mat-button-filled-container-color: var(--mat-sys-error);
      --mat-button-filled-label-text-color: var(--mat-sys-on-error);
    }
  `,
})
export class HostDetail {
  private readonly api = inject(AdminApi);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);

  protected readonly host = signal<AdminHost | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  private hostId = '';

  constructor() {
    inject(ActivatedRoute)
      .paramMap.pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe((params) => {
        this.hostId = params.get('id') ?? '';
        this.host.set(null);
        this.load();
      });
  }

  protected ban(host: AdminHost): void {
    askReason(this.dialog, {
      title: 'Ban host',
      message: 'Banning hides every event this host has published and blocks sign-in.',
      confirmLabel: 'Ban',
      destructive: true,
    })
      .pipe(switchMap((reason) => this.api.banHost(host.id, reason)))
      .subscribe({
        next: (result) => {
          this.notifier.success(`Banned. ${result.eventsHidden} ${result.eventsHidden === 1 ? 'event' : 'events'} hidden.`);
          this.load();
        },
        error: (err: unknown) => this.error.set(adminErrorMessage(err, NOT_FOUND)),
      });
  }

  protected unban(host: AdminHost): void {
    askReason(this.dialog, { title: 'Unban host', confirmLabel: 'Unban' })
      .pipe(switchMap((reason) => this.api.unbanHost(host.id, reason)))
      .subscribe({
        next: () => {
          this.notifier.success('Host unbanned.');
          this.load();
        },
        error: (err: unknown) => this.error.set(adminErrorMessage(err, NOT_FOUND)),
      });
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.host(this.hostId).subscribe({
      next: (host) => {
        this.host.set(host);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(adminErrorMessage(err, NOT_FOUND));
        this.loading.set(false);
      },
    });
  }
}
