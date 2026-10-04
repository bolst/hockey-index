import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatBottomSheetRef, MAT_BOTTOM_SHEET_DATA } from '@angular/material/bottom-sheet';
import { MatButton } from '@angular/material/button';
import { DiscoverFilters } from './discover-filters';
import { DEFAULT_FILTERS, SearchFilters } from './search-filters';

export interface FiltersSheetData {
  filters: SearchFilters;
  apply: (filters: SearchFilters) => void;
}

/** Mobile filter panel. Changes apply as they are made so results update behind the sheet. */
@Component({
  selector: 'app-filters-sheet',
  imports: [DiscoverFilters, MatButton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 class="title" id="filters-sheet-title">Filters</h2>
    <app-discover-filters [stacked]="true" [filters]="filters()" (filtersChange)="change($event)" />
    <div class="actions">
      <button matButton type="button" (click)="change(defaults)">Reset</button>
      <button matButton="filled" type="button" (click)="close()">Show results</button>
    </div>
  `,
  styles: `
    :host {
      display: block;
      padding: 8px 8px 16px;
    }

    .title {
      margin: 0 0 20px;
      font: var(--mat-sys-title-large);
    }

    .actions {
      display: flex;
      justify-content: space-between;
      margin-top: 24px;
    }
  `,
})
export class FiltersSheet {
  private readonly data = inject<FiltersSheetData>(MAT_BOTTOM_SHEET_DATA);
  private readonly sheetRef = inject(MatBottomSheetRef);
  protected readonly defaults = DEFAULT_FILTERS;
  protected readonly filters = signal(this.data.filters);

  protected change(filters: SearchFilters): void {
    this.filters.set(filters);
    this.data.apply(filters);
  }

  protected close(): void {
    this.sheetRef.dismiss();
  }
}
