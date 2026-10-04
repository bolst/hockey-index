import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { AdminApi, AuditEntry } from '../../../core/api/admin-api';
import { InlineMessage } from '../../../shared/inline-message/inline-message';
import { adminErrorMessage } from '../admin-errors';
import { AuditDetailsDialog } from './audit-details-dialog';

@Component({
  selector: 'app-admin-audit',
  imports: [DatePipe, MatButton, MatIconButton, MatIcon, MatProgressBar, MatTableModule, InlineMessage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'app-stack' },
  template: `
    <h2 class="section-title">Audit log</h2>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading audit log" />
    }
    @if (error(); as message) {
      <app-inline-message kind="error">{{ message }}</app-inline-message>
    }
    <div class="table-wrap">
      <table mat-table [dataSource]="entries()" aria-label="Audit entries">
        <ng-container matColumnDef="time">
          <th mat-header-cell *matHeaderCellDef>Time</th>
          <td mat-cell *matCellDef="let row">{{ row.createdAt | date: 'medium' }}</td>
        </ng-container>
        <ng-container matColumnDef="action">
          <th mat-header-cell *matHeaderCellDef>Action</th>
          <td mat-cell *matCellDef="let row">{{ row.action }}</td>
        </ng-container>
        <ng-container matColumnDef="target">
          <th mat-header-cell *matHeaderCellDef class="secondary">Target</th>
          <td mat-cell *matCellDef="let row" class="secondary id">{{ row.targetType }} {{ row.targetId }}</td>
        </ng-container>
        <ng-container matColumnDef="actor">
          <th mat-header-cell *matHeaderCellDef class="secondary">Actor</th>
          <td mat-cell *matCellDef="let row" class="secondary id">{{ row.actorId }}</td>
        </ng-container>
        <ng-container matColumnDef="reason">
          <th mat-header-cell *matHeaderCellDef>Reason</th>
          <td mat-cell *matCellDef="let row">{{ row.reason }}</td>
        </ng-container>
        <ng-container matColumnDef="details">
          <th mat-header-cell *matHeaderCellDef><span class="visually-hidden">Details</span></th>
          <td mat-cell *matCellDef="let row">
            <button matIconButton type="button" aria-label="Show details" (click)="showDetails(row)">
              <mat-icon aria-hidden="true">data_object</mat-icon>
            </button>
          </td>
        </ng-container>
        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns" [attr.data-testid]="'audit-row-' + row.id"></tr>
        <tr class="mat-mdc-row" *matNoDataRow>
          <td class="mat-mdc-cell" [attr.colspan]="columns.length">{{ loading() ? 'Loading…' : 'No audit entries.' }}</td>
        </tr>
      </table>
    </div>
    @if (nextCursor() !== null) {
      <div class="app-actions">
        <button matButton="outlined" type="button" [disabled]="loading()" (click)="loadMore()">Load more</button>
      </div>
    }
  `,
  styles: `
    .section-title {
      margin: 0;
      font: var(--mat-sys-title-large);
    }
    .table-wrap {
      overflow-x: auto;
    }
    .id {
      overflow-wrap: anywhere;
      font: var(--mat-sys-body-small);
    }
    @media (max-width: 599.98px) {
      .secondary {
        display: none;
      }
    }
  `,
})
export class Audit {
  private readonly api = inject(AdminApi);
  private readonly dialog = inject(MatDialog);

  protected readonly columns = ['time', 'action', 'target', 'actor', 'reason', 'details'];
  protected readonly entries = signal<AuditEntry[]>([]);
  protected readonly nextCursor = signal<number | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.fetch(null);
  }

  protected loadMore(): void {
    this.fetch(this.nextCursor());
  }

  protected showDetails(entry: AuditEntry): void {
    this.dialog.open(AuditDetailsDialog, { data: entry, width: '640px', maxWidth: 'calc(100vw - 32px)' });
  }

  private fetch(cursor: number | null): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.audit(cursor).subscribe({
      next: (page) => {
        this.entries.update((existing) => (cursor === null ? page.items : [...existing, ...page.items]));
        this.nextCursor.set(page.nextCursor);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(adminErrorMessage(err));
        this.loading.set(false);
      },
    });
  }
}
