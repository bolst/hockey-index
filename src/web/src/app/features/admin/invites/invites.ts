import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatIcon } from '@angular/material/icon';
import { MatInput } from '@angular/material/input';
import { MatPaginator, PageEvent } from '@angular/material/paginator';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { switchMap } from 'rxjs';
import { AdminApi, Invite } from '../../../core/api/admin-api';
import { formatPhone } from '../../../core/format/phone';
import { PhonePipe } from '../../../core/format/phone.pipe';
import { InlineMessage } from '../../../shared/inline-message/inline-message';
import { Notifier } from '../../../shared/notifier/notifier';
import { REASON_MAX_LENGTH, askReason } from '../../../shared/reason-dialog/reason-dialog';
import { StatusChip, StatusChipTone } from '../../../shared/status-chip/status-chip';
import { adminErrorMessage } from '../admin-errors';

const PAGE_SIZE = 25;
const E164 = /^\+[1-9]\d{7,14}$/;
const PHONE_SEPARATORS = /[\s().-]/g;

function toE164(input: string): string {
  return input.replace(PHONE_SEPARATORS, '');
}

const phoneWithCountryCode: ValidatorFn = (control) =>
  !control.value || E164.test(toE164(control.value)) ? null : { phone: true };

interface InviteState {
  label: string;
  tone: StatusChipTone;
}

@Component({
  selector: 'app-admin-invites',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    MatButton,
    MatIconButton,
    MatIcon,
    MatFormField,
    MatLabel,
    MatHint,
    MatError,
    MatInput,
    MatTableModule,
    MatPaginator,
    MatProgressBar,
    PhonePipe,
    InlineMessage,
    StatusChip,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'app-stack app-stack--lg' },
  template: `
    <h2 class="section-title">Invites</h2>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading invites" />
    }
    <form class="create" [formGroup]="form" (submit)="$event.preventDefault(); create()" novalidate aria-label="Create invite">
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>Phone</mat-label>
        <input matInput type="tel" formControlName="phone" autocomplete="off" placeholder="+1 416-555-0142" />
        <mat-hint>Include the country code, for example +1 416-555-0142.</mat-hint>
        <mat-error>Enter the number with its country code, like +1 416-555-0142.</mat-error>
      </mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="days">
        <mat-label>Expires in (days)</mat-label>
        <input matInput type="number" min="1" max="90" formControlName="expiresInDays" />
        <mat-error>Enter 1 to 90 days.</mat-error>
      </mat-form-field>
      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="wide">
        <mat-label>Reason</mat-label>
        <input matInput formControlName="reason" [maxlength]="reasonMax" />
        <mat-error>Enter a reason.</mat-error>
      </mat-form-field>
      <button matButton="filled" type="submit" [disabled]="saving()">
        <mat-icon aria-hidden="true">person_add</mat-icon>
        Create invite
      </button>
    </form>
    @if (error(); as message) {
      <app-inline-message kind="error">{{ message }}</app-inline-message>
    }
    <div class="table-wrap">
      <table mat-table [dataSource]="pageRows()" aria-label="Invites">
        <ng-container matColumnDef="phone">
          <th mat-header-cell *matHeaderCellDef>Phone</th>
          <td mat-cell *matCellDef="let row">{{ row.phone | phone }}</td>
        </ng-container>
        <ng-container matColumnDef="status">
          <th mat-header-cell *matHeaderCellDef>Status</th>
          <td mat-cell *matCellDef="let row">
            @let state = stateOf(row);
            <app-status-chip [label]="state.label" [tone]="state.tone" />
          </td>
        </ng-container>
        <ng-container matColumnDef="expires">
          <th mat-header-cell *matHeaderCellDef class="secondary">Expires</th>
          <td mat-cell *matCellDef="let row" class="secondary">{{ row.expiresAt | date: 'medium' }}</td>
        </ng-container>
        <ng-container matColumnDef="createdBy">
          <th mat-header-cell *matHeaderCellDef class="secondary">Created by</th>
          <td mat-cell *matCellDef="let row" class="secondary id">{{ row.createdBy ?? 'Unknown' }}</td>
        </ng-container>
        <ng-container matColumnDef="actions">
          <th mat-header-cell *matHeaderCellDef><span class="visually-hidden">Actions</span></th>
          <td mat-cell *matCellDef="let row">
            @if (!row.usedAt) {
              <button matIconButton type="button" [attr.aria-label]="'Revoke invite for ' + (row.phone | phone)" (click)="revoke(row)">
                <mat-icon aria-hidden="true">delete</mat-icon>
              </button>
            }
          </td>
        </ng-container>
        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns" [attr.data-testid]="'invite-row-' + row.id"></tr>
        <tr class="mat-mdc-row" *matNoDataRow>
          <td class="mat-mdc-cell" [attr.colspan]="columns.length">No invites.</td>
        </tr>
      </table>
    </div>
    <mat-paginator
      [length]="invites().length"
      [pageSize]="pageSize"
      [pageIndex]="pageIndex()"
      [hidePageSize]="true"
      (page)="onPage($event)"
      aria-label="Invite pages"
    />
  `,
  styles: `
    .section-title {
      margin: 0;
      font: var(--mat-sys-title-large);
    }
    .create {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-start;
      gap: 8px 16px;
    }
    .create mat-form-field {
      flex: 1 1 200px;
    }
    .create .days {
      flex: 0 1 160px;
    }
    .create .wide {
      flex-basis: 280px;
    }
    .create button {
      margin-top: 8px;
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
export class Invites {
  private readonly api = inject(AdminApi);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);

  protected readonly pageSize = PAGE_SIZE;
  protected readonly reasonMax = REASON_MAX_LENGTH;
  protected readonly columns = ['phone', 'status', 'expires', 'createdBy', 'actions'];
  protected readonly invites = signal<Invite[]>([]);
  protected readonly pageIndex = signal(0);
  protected readonly pageRows = computed(() =>
    this.invites().slice(this.pageIndex() * PAGE_SIZE, (this.pageIndex() + 1) * PAGE_SIZE),
  );
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = new FormGroup({
    phone: new FormControl('', { nonNullable: true, validators: [Validators.required, phoneWithCountryCode] }),
    expiresInDays: new FormControl(14, {
      nonNullable: true,
      validators: [Validators.required, Validators.min(1), Validators.max(90)],
    }),
    reason: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/\S/)] }),
  });

  constructor() {
    this.load();
  }

  protected stateOf(invite: Invite): InviteState {
    if (invite.usedAt) {
      return { label: 'Used', tone: 'neutral' };
    }
    if (Date.parse(invite.expiresAt) <= Date.now()) {
      return { label: 'Expired', tone: 'critical' };
    }
    return { label: 'Active', tone: 'positive' };
  }

  protected onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
  }

  protected create(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { phone, expiresInDays, reason } = this.form.getRawValue();
    this.saving.set(true);
    this.error.set(null);
    this.api.createInvite(toE164(phone), expiresInDays, reason.trim()).subscribe({
      next: () => {
        this.saving.set(false);
        this.form.reset();
        this.notifier.success('Invite created.');
        this.load();
      },
      error: (err: unknown) => {
        this.saving.set(false);
        this.error.set(adminErrorMessage(err));
      },
    });
  }

  protected revoke(invite: Invite): void {
    askReason(this.dialog, { title: 'Revoke invite', message: formatPhone(invite.phone), confirmLabel: 'Revoke', destructive: true })
      .pipe(switchMap((reason) => this.api.revokeInvite(invite.id, reason)))
      .subscribe({
        next: () => {
          this.notifier.success('Invite revoked.');
          this.load();
        },
        error: (err: unknown) => this.error.set(adminErrorMessage(err, 'That invite was already revoked.')),
      });
  }

  private load(): void {
    this.loading.set(true);
    this.api.invites().subscribe({
      next: (invites) => {
        this.invites.set(invites);
        this.pageIndex.set(Math.min(this.pageIndex(), Math.max(0, Math.ceil(invites.length / PAGE_SIZE) - 1)));
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(adminErrorMessage(err));
        this.loading.set(false);
      },
    });
  }
}
