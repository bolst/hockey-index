import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatCard, MatCardActions, MatCardContent, MatCardHeader, MatCardTitle } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatProgressBar } from '@angular/material/progress-bar';
import { Observer, filter, switchMap } from 'rxjs';
import { AdminApi, Breaker, BreakerName } from '../../../core/api/admin-api';
import { InlineMessage } from '../../../shared/inline-message/inline-message';
import { Notifier } from '../../../shared/notifier/notifier';
import { askReason } from '../../../shared/reason-dialog/reason-dialog';
import { StatusChip } from '../../../shared/status-chip/status-chip';
import { adminErrorMessage } from '../admin-errors';
import { OpenBreakerData, OpenBreakerDialog, OpenBreakerResult } from './open-breaker-dialog';

const LABELS: Record<BreakerName, string> = {
  otp_sms: 'SMS codes',
  mapbox: 'Address lookup',
  webrisk: 'Link scanning',
};

@Component({
  selector: 'app-admin-breakers',
  imports: [
    DatePipe,
    MatButton,
    MatCard,
    MatCardHeader,
    MatCardTitle,
    MatCardContent,
    MatCardActions,
    MatProgressBar,
    InlineMessage,
    StatusChip,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'app-stack' },
  template: `
    <h2 class="section-title">Circuit breakers</h2>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading breakers" />
    }
    @if (error(); as message) {
      <app-inline-message kind="error">{{ message }}</app-inline-message>
    }
    <div class="grid">
      @for (breaker of breakers(); track breaker.name) {
        <mat-card appearance="outlined" [attr.data-testid]="'breaker-' + breaker.name">
          <mat-card-header>
            <mat-card-title>{{ label(breaker.name) }}</mat-card-title>
          </mat-card-header>
          <mat-card-content class="app-stack">
            @if (breaker.isOpen) {
              <app-status-chip label="Open — tripped" tone="critical" />
              <dl class="facts">
                @if (breaker.openUntil) {
                  <dt>Open until</dt>
                  <dd>{{ breaker.openUntil | date: 'medium' }}</dd>
                }
                @if (breaker.reason) {
                  <dt>Reason</dt>
                  <dd>{{ breaker.reason }}</dd>
                }
                @if (breaker.openedBy) {
                  <dt>Opened by</dt>
                  <dd class="id">{{ breaker.openedBy }}</dd>
                }
              </dl>
            } @else {
              <app-status-chip label="Closed — normal" tone="positive" />
            }
          </mat-card-content>
          <mat-card-actions>
            @if (breaker.isOpen) {
              <button matButton="outlined" type="button" (click)="close(breaker)">Close</button>
            } @else {
              <button matButton="outlined" type="button" (click)="open(breaker)">Open</button>
            }
          </mat-card-actions>
        </mat-card>
      }
    </div>
  `,
  styles: `
    .section-title {
      margin: 0;
      font: var(--mat-sys-title-large);
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 280px), 1fr));
      gap: 16px;
    }
    mat-card-actions {
      padding: 0 16px 16px;
    }
    .facts {
      display: grid;
      grid-template-columns: max-content minmax(0, 1fr);
      gap: 4px 12px;
      margin: 0;
    }
    dt {
      color: var(--mat-sys-on-surface-variant);
    }
    dd {
      margin: 0;
    }
    .id {
      overflow-wrap: anywhere;
      font: var(--mat-sys-body-small);
    }
  `,
})
export class Breakers {
  private readonly api = inject(AdminApi);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);

  protected readonly breakers = signal<Breaker[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.load();
  }

  protected label(name: BreakerName): string {
    return LABELS[name] ?? name;
  }

  protected open(breaker: Breaker): void {
    const label = this.label(breaker.name);
    this.dialog
      .open<OpenBreakerDialog, OpenBreakerData, OpenBreakerResult>(OpenBreakerDialog, {
        data: { label },
        width: '480px',
        maxWidth: 'calc(100vw - 32px)',
      })
      .afterClosed()
      .pipe(
        filter((result): result is OpenBreakerResult => !!result),
        switchMap(({ minutes, reason }) => this.api.changeBreaker(breaker.name, 'open', reason, minutes)),
      )
      .subscribe(this.handle(`${label} breaker opened.`));
  }

  protected close(breaker: Breaker): void {
    const label = this.label(breaker.name);
    askReason(this.dialog, { title: `Close breaker: ${label}`, confirmLabel: 'Close breaker' })
      .pipe(switchMap((reason) => this.api.changeBreaker(breaker.name, 'close', reason)))
      .subscribe(this.handle(`${label} breaker closed.`));
  }

  private handle(done: string): Partial<Observer<unknown>> {
    return {
      next: () => {
        this.notifier.success(done);
        this.load();
      },
      error: (err: unknown) => this.error.set(adminErrorMessage(err)),
    };
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.breakers().subscribe({
      next: (breakers) => {
        this.breakers.set(breakers);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(adminErrorMessage(err));
        this.loading.set(false);
      },
    });
  }
}
