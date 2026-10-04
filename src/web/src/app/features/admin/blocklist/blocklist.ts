import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatError, MatFormField, MatLabel } from '@angular/material/form-field';
import { MatIcon } from '@angular/material/icon';
import { MatInput } from '@angular/material/input';
import { MatPaginator, PageEvent } from '@angular/material/paginator';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatOption, MatSelect } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { switchMap } from 'rxjs';
import { AdminApi, BlocklistEntry, BlocklistKind } from '../../../core/api/admin-api';
import { InlineMessage } from '../../../shared/inline-message/inline-message';
import { Notifier } from '../../../shared/notifier/notifier';
import { REASON_MAX_LENGTH, askReason } from '../../../shared/reason-dialog/reason-dialog';
import { adminErrorMessage } from '../admin-errors';

const PAGE_SIZE = 25;

@Component({
  selector: 'app-admin-blocklist',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    MatButton,
    MatIconButton,
    MatIcon,
    MatFormField,
    MatLabel,
    MatError,
    MatInput,
    MatSelect,
    MatOption,
    MatTableModule,
    MatPaginator,
    MatProgressBar,
    InlineMessage,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'app-stack app-stack--lg' },
  template: `
    <h2 class="section-title">Blocklist</h2>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading blocklist" />
    }
    <form class="add" [formGroup]="form" (submit)="$event.preventDefault(); add()" novalidate aria-label="Add to blocklist">
      <mat-form-field appearance="outline">
        <mat-label>Kind</mat-label>
        <mat-select formControlName="kind">
          <mat-option value="domain">Domain</mat-option>
          <mat-option value="phone">Phone</mat-option>
        </mat-select>
      </mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>Value</mat-label>
        <input matInput formControlName="value" autocomplete="off" spellcheck="false" />
        <mat-error>Enter a domain or phone number.</mat-error>
      </mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="wide">
        <mat-label>Reason</mat-label>
        <input matInput formControlName="reason" [maxlength]="reasonMax" />
        <mat-error>Enter a reason.</mat-error>
      </mat-form-field>
      <button matButton="filled" type="submit" [disabled]="saving()">
        <mat-icon aria-hidden="true">add</mat-icon>
        Add
      </button>
    </form>
    @if (error(); as message) {
      <app-inline-message kind="error">{{ message }}</app-inline-message>
    }
    <div class="table-wrap">
      <table mat-table [dataSource]="pageRows()" aria-label="Blocklist entries">
        <ng-container matColumnDef="kind">
          <th mat-header-cell *matHeaderCellDef>Kind</th>
          <td mat-cell *matCellDef="let row">{{ row.kind }}</td>
        </ng-container>
        <ng-container matColumnDef="value">
          <th mat-header-cell *matHeaderCellDef>Value</th>
          <td mat-cell *matCellDef="let row" class="value">{{ row.value }}</td>
        </ng-container>
        <ng-container matColumnDef="reason">
          <th mat-header-cell *matHeaderCellDef class="secondary">Reason</th>
          <td mat-cell *matCellDef="let row" class="secondary">{{ row.reason }}</td>
        </ng-container>
        <ng-container matColumnDef="created">
          <th mat-header-cell *matHeaderCellDef class="secondary">Created</th>
          <td mat-cell *matCellDef="let row" class="secondary">{{ row.createdAt | date: 'medium' }}</td>
        </ng-container>
        <ng-container matColumnDef="actions">
          <th mat-header-cell *matHeaderCellDef><span class="visually-hidden">Actions</span></th>
          <td mat-cell *matCellDef="let row">
            <button matIconButton type="button" [attr.aria-label]="'Remove ' + row.value" (click)="remove(row)">
              <mat-icon aria-hidden="true">delete</mat-icon>
            </button>
          </td>
        </ng-container>
        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns" [attr.data-testid]="'blocklist-row-' + row.id"></tr>
        <tr class="mat-mdc-row" *matNoDataRow>
          <td class="mat-mdc-cell" [attr.colspan]="columns.length">No blocklist entries.</td>
        </tr>
      </table>
    </div>
    <mat-paginator
      [length]="entries().length"
      [pageSize]="pageSize"
      [pageIndex]="pageIndex()"
      [hidePageSize]="true"
      (page)="onPage($event)"
      aria-label="Blocklist pages"
    />
  `,
  styles: `
    .section-title {
      margin: 0;
      font: var(--mat-sys-title-large);
    }
    .add {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-start;
      gap: 8px 16px;
    }
    .add mat-form-field {
      flex: 1 1 160px;
    }
    .add .wide {
      flex-basis: 280px;
    }
    .add button {
      margin-top: 8px;
    }
    .table-wrap {
      overflow-x: auto;
    }
    .value {
      overflow-wrap: anywhere;
    }
    @media (max-width: 599.98px) {
      .secondary {
        display: none;
      }
    }
  `,
})
export class Blocklist {
  private readonly api = inject(AdminApi);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);

  protected readonly pageSize = PAGE_SIZE;
  protected readonly reasonMax = REASON_MAX_LENGTH;
  protected readonly columns = ['kind', 'value', 'reason', 'created', 'actions'];
  protected readonly entries = signal<BlocklistEntry[]>([]);
  protected readonly pageIndex = signal(0);
  protected readonly pageRows = computed(() =>
    this.entries().slice(this.pageIndex() * PAGE_SIZE, (this.pageIndex() + 1) * PAGE_SIZE),
  );
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = new FormGroup({
    kind: new FormControl<BlocklistKind>('domain', { nonNullable: true }),
    value: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/\S/)] }),
    reason: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/\S/)] }),
  });

  constructor() {
    this.load();
  }

  protected onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
  }

  protected add(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { kind, value, reason } = this.form.getRawValue();
    this.saving.set(true);
    this.error.set(null);
    this.api.addBlocklistEntry(kind, value.trim(), reason.trim()).subscribe({
      next: (entry) => {
        this.saving.set(false);
        this.form.reset({ kind });
        this.notifier.success(`Blocked ${entry.value}.`);
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.error.set(adminErrorMessage(err));
      },
    });
  }

  protected remove(entry: BlocklistEntry): void {
    askReason(this.dialog, {
      title: 'Remove from blocklist',
      message: entry.value,
      confirmLabel: 'Remove',
      destructive: true,
    })
      .pipe(switchMap((reason) => this.api.removeBlocklistEntry(entry.id, reason)))
      .subscribe({
        next: () => {
          this.notifier.success(`Removed ${entry.value}.`);
          this.load();
        },
        error: (err: unknown) => this.error.set(adminErrorMessage(err, 'That entry was already removed.')),
      });
  }

  private load(): void {
    this.loading.set(true);
    this.api.blocklist().subscribe({
      next: (entries) => {
        this.entries.set(entries);
        this.pageIndex.set(Math.min(this.pageIndex(), Math.max(0, Math.ceil(entries.length / PAGE_SIZE) - 1)));
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(adminErrorMessage(err));
        this.loading.set(false);
      },
    });
  }
}
