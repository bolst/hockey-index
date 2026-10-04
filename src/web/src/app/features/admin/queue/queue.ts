import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatCard, MatCardActions, MatCardContent, MatCardHeader, MatCardTitle } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import {
  MatExpansionPanel,
  MatExpansionPanelHeader,
  MatExpansionPanelTitle,
} from '@angular/material/expansion';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { Observable, switchMap } from 'rxjs';
import { AdminApi, AdminEventSummary, QueueItem } from '../../../core/api/admin-api';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { InlineMessage } from '../../../shared/inline-message/inline-message';
import { Notifier } from '../../../shared/notifier/notifier';
import { askReason } from '../../../shared/reason-dialog/reason-dialog';
import { StatusChip, StatusChipTone } from '../../../shared/status-chip/status-chip';
import { adminErrorMessage } from '../admin-errors';

interface QueueAction {
  title: string;
  confirmLabel: string;
  destructive?: boolean;
  done: string;
  call: (publicId: string, reason: string) => Observable<{ event: AdminEventSummary }>;
}

@Component({
  selector: 'app-admin-queue',
  imports: [
    DatePipe,
    RouterLink,
    MatButton,
    MatIcon,
    MatCard,
    MatCardHeader,
    MatCardTitle,
    MatCardContent,
    MatCardActions,
    MatExpansionPanel,
    MatExpansionPanelHeader,
    MatExpansionPanelTitle,
    MatProgressBar,
    EmptyState,
    InlineMessage,
    StatusChip,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'app-stack' },
  template: `
    <h2 class="section-title">Moderation queue</h2>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading queue" />
    }
    @if (error(); as message) {
      <app-inline-message kind="error">{{ message }}</app-inline-message>
    }
    @if (items(); as list) {
      @for (item of list; track item.publicId) {
        <mat-card appearance="outlined" [attr.data-testid]="'queue-item-' + item.publicId">
          <mat-card-header>
            <mat-card-title>
              <a [routerLink]="['/e', item.publicId]">{{ item.title }}</a>
            </mat-card-title>
          </mat-card-header>
          <mat-card-content class="app-stack">
            <div class="meta">
              <app-status-chip [label]="item.status" [tone]="statusTone(item.status)" />
              <span>{{ item.unreviewedReports }} unreviewed {{ item.unreviewedReports === 1 ? 'report' : 'reports' }}</span>
              <a [routerLink]="['/admin/hosts', item.hostId]">View host</a>
            </div>
            @if (item.hiddenReason) {
              <p class="reason">Hidden reason: {{ item.hiddenReason }}</p>
            }
            @if (item.reports.length) {
              <mat-expansion-panel>
                <mat-expansion-panel-header>
                  <mat-panel-title>Reports ({{ item.reports.length }})</mat-panel-title>
                </mat-expansion-panel-header>
                <ul class="reports">
                  @for (report of item.reports; track $index) {
                    <li>
                      <strong>{{ report.reason }}</strong>
                      <span class="when">{{ report.createdAt | date: 'medium' }}</span>
                      @if (report.details) {
                        <p>{{ report.details }}</p>
                      }
                    </li>
                  }
                </ul>
              </mat-expansion-panel>
            }
          </mat-card-content>
          <mat-card-actions class="app-actions">
            @if (item.status === 'hidden') {
              <button matButton="outlined" type="button" (click)="run(restore, item)">
                <mat-icon aria-hidden="true">visibility</mat-icon>
                Restore
              </button>
            } @else {
              <button matButton="outlined" type="button" (click)="run(hide, item)">
                <mat-icon aria-hidden="true">visibility_off</mat-icon>
                Hide
              </button>
            }
            @if (item.hiddenReason) {
              <button matButton type="button" (click)="run(clearReason, item)">Clear hidden reason</button>
            }
          </mat-card-actions>
        </mat-card>
      } @empty {
        <app-empty-state icon="task_alt" headline="Nothing to review">
          <p>Reported and hidden events appear here.</p>
        </app-empty-state>
      }
    }
  `,
  styles: `
    .section-title {
      margin: 0;
      font: var(--mat-sys-title-large);
    }
    .meta {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 8px 16px;
    }
    .reason,
    .reports p {
      margin: 0;
    }
    .reports {
      margin: 0;
      padding-left: 20px;
    }
    .when {
      margin-left: 8px;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
  `,
})
export class Queue {
  private readonly api = inject(AdminApi);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);

  protected readonly items = signal<QueueItem[] | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly hide: QueueAction = {
    title: 'Hide event',
    confirmLabel: 'Hide',
    destructive: true,
    done: 'Event hidden.',
    call: (id, reason) => this.api.hideEvent(id, reason),
  };
  protected readonly restore: QueueAction = {
    title: 'Restore event',
    confirmLabel: 'Restore',
    done: 'Event restored.',
    call: (id, reason) => this.api.restoreEvent(id, reason),
  };
  protected readonly clearReason: QueueAction = {
    title: 'Clear hidden reason',
    confirmLabel: 'Clear',
    done: 'Hidden reason cleared.',
    call: (id, reason) => this.api.clearHiddenReason(id, reason),
  };

  constructor() {
    this.load();
  }

  protected statusTone(status: string): StatusChipTone {
    return status === 'hidden' ? 'critical' : status === 'published' ? 'positive' : 'neutral';
  }

  protected run(action: QueueAction, item: QueueItem): void {
    askReason(this.dialog, { title: action.title, message: item.title, confirmLabel: action.confirmLabel, destructive: action.destructive })
      .pipe(switchMap((reason) => action.call(item.publicId, reason)))
      .subscribe({
        next: () => {
          this.notifier.success(action.done);
          this.load();
        },
        error: (err: unknown) => this.error.set(adminErrorMessage(err, 'That event no longer exists.')),
      });
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.queue().subscribe({
      next: (items) => {
        this.items.set(items);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(adminErrorMessage(err));
        this.loading.set(false);
      },
    });
  }
}
