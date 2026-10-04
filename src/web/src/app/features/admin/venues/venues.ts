import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatIcon } from '@angular/material/icon';
import { MatInput } from '@angular/material/input';
import { MatPaginator, PageEvent } from '@angular/material/paginator';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { filter, switchMap } from 'rxjs';
import { AdminApi, AdminVenue } from '../../../core/api/admin-api';
import { InlineMessage } from '../../../shared/inline-message/inline-message';
import { Notifier } from '../../../shared/notifier/notifier';
import { adminErrorMessage } from '../admin-errors';
import { VenueEditDialog, VenueEditResult } from './venue-edit-dialog';
import { VenueMergeDialog, VenueMergeResult } from './venue-merge-dialog';

const PAGE_SIZE = 25;
const DIALOG_SIZE = { width: '560px', maxWidth: 'calc(100vw - 32px)' };

@Component({
  selector: 'app-admin-venues',
  imports: [
    ReactiveFormsModule,
    MatButton,
    MatIconButton,
    MatIcon,
    MatFormField,
    MatLabel,
    MatInput,
    MatTableModule,
    MatPaginator,
    MatProgressBar,
    InlineMessage,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'app-stack' },
  template: `
    <h2 class="section-title">Venues</h2>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" aria-label="Loading venues" />
    }
    <form class="search" role="search" (submit)="$event.preventDefault(); search()">
      <mat-form-field appearance="outline">
        <mat-label>Search venues</mat-label>
        <input matInput type="search" [formControl]="query" autocomplete="off" />
      </mat-form-field>
      <button matButton="filled" type="submit">
        <mat-icon aria-hidden="true">search</mat-icon>
        Search
      </button>
    </form>
    @if (error(); as message) {
      <app-inline-message kind="error">{{ message }}</app-inline-message>
    }
    <div class="table-wrap">
      <table mat-table [dataSource]="pageRows()" aria-label="Venues">
        <ng-container matColumnDef="name">
          <th mat-header-cell *matHeaderCellDef>Name</th>
          <td mat-cell *matCellDef="let row">
            {{ row.name }}
            <div class="id">{{ row.publicId }}</div>
          </td>
        </ng-container>
        <ng-container matColumnDef="address">
          <th mat-header-cell *matHeaderCellDef class="secondary">Address</th>
          <td mat-cell *matCellDef="let row" class="secondary">{{ row.addressLine }}, {{ row.city }}</td>
        </ng-container>
        <ng-container matColumnDef="region">
          <th mat-header-cell *matHeaderCellDef class="secondary">Region</th>
          <td mat-cell *matCellDef="let row" class="secondary">{{ row.region }}</td>
        </ng-container>
        <ng-container matColumnDef="country">
          <th mat-header-cell *matHeaderCellDef class="secondary">Country</th>
          <td mat-cell *matCellDef="let row" class="secondary">{{ row.country }}</td>
        </ng-container>
        <ng-container matColumnDef="timeZone">
          <th mat-header-cell *matHeaderCellDef class="secondary">Time zone</th>
          <td mat-cell *matCellDef="let row" class="secondary">{{ row.timeZone }}</td>
        </ng-container>
        <ng-container matColumnDef="actions">
          <th mat-header-cell *matHeaderCellDef><span class="visually-hidden">Actions</span></th>
          <td mat-cell *matCellDef="let row" class="actions">
            <button matIconButton type="button" [attr.aria-label]="'Edit ' + row.name" (click)="edit(row)">
              <mat-icon aria-hidden="true">edit</mat-icon>
            </button>
            <button matIconButton type="button" [attr.aria-label]="'Merge ' + row.name" (click)="merge(row)">
              <mat-icon aria-hidden="true">merge</mat-icon>
            </button>
          </td>
        </ng-container>
        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns" [attr.data-testid]="'venue-row-' + row.publicId"></tr>
        <tr class="mat-mdc-row" *matNoDataRow>
          <td class="mat-mdc-cell" [attr.colspan]="columns.length">No venues found.</td>
        </tr>
      </table>
    </div>
    <mat-paginator
      [length]="venues().length"
      [pageSize]="pageSize"
      [pageIndex]="pageIndex()"
      [hidePageSize]="true"
      (page)="onPage($event)"
      aria-label="Venue pages"
    />
  `,
  styles: `
    .section-title {
      margin: 0;
      font: var(--mat-sys-title-large);
    }
    .search {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-start;
      gap: 8px 16px;
    }
    .search mat-form-field {
      flex: 1 1 240px;
    }
    .search button {
      margin-top: 8px;
    }
    .table-wrap {
      overflow-x: auto;
    }
    .id {
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
    .actions {
      white-space: nowrap;
    }
    @media (max-width: 599.98px) {
      .secondary {
        display: none;
      }
    }
  `,
})
export class Venues {
  private readonly api = inject(AdminApi);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);

  protected readonly pageSize = PAGE_SIZE;
  protected readonly columns = ['name', 'address', 'region', 'country', 'timeZone', 'actions'];
  protected readonly query = new FormControl('', { nonNullable: true });
  protected readonly venues = signal<AdminVenue[]>([]);
  protected readonly pageIndex = signal(0);
  protected readonly pageRows = computed(() =>
    this.venues().slice(this.pageIndex() * PAGE_SIZE, (this.pageIndex() + 1) * PAGE_SIZE),
  );
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.search();
  }

  protected onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
  }

  protected search(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.venues(this.query.value).subscribe({
      next: (venues) => {
        this.venues.set(venues);
        this.pageIndex.set(0);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.error.set(adminErrorMessage(err));
        this.loading.set(false);
      },
    });
  }

  protected edit(venue: AdminVenue): void {
    this.dialog
      .open<VenueEditDialog, AdminVenue, VenueEditResult>(VenueEditDialog, { data: venue, ...DIALOG_SIZE })
      .afterClosed()
      .pipe(
        filter((result): result is VenueEditResult => !!result),
        switchMap(({ edit, reason }) => this.api.updateVenue(venue.publicId, edit, reason)),
      )
      .subscribe({
        next: (updated) => {
          this.venues.update((list) => list.map((v) => (v.publicId === updated.publicId ? updated : v)));
          this.notifier.success(`Saved ${updated.name}.`);
        },
        error: (err: unknown) => this.error.set(adminErrorMessage(err, 'That venue no longer exists.')),
      });
  }

  protected merge(venue: AdminVenue): void {
    this.dialog
      .open<VenueMergeDialog, AdminVenue, VenueMergeResult>(VenueMergeDialog, { data: venue, ...DIALOG_SIZE })
      .afterClosed()
      .pipe(
        filter((result): result is VenueMergeResult => !!result),
        switchMap(({ intoId, reason }) => this.api.mergeVenue(venue.publicId, intoId, reason)),
      )
      .subscribe({
        next: (target) => {
          this.notifier.success(`Merged ${venue.name} into ${target.name}.`);
          this.search();
        },
        error: (err: unknown) => this.error.set(adminErrorMessage(err, 'One of those venues no longer exists.')),
      });
  }
}
