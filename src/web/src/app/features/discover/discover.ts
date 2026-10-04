import { BreakpointObserver } from '@angular/cdk/layout';
import { DOCUMENT } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatBadge } from '@angular/material/badge';
import { MatBottomSheet } from '@angular/material/bottom-sheet';
import { MatButton } from '@angular/material/button';
import { MatButtonToggle, MatButtonToggleGroup } from '@angular/material/button-toggle';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { ActivatedRoute, Router } from '@angular/router';
import { map } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SearchResponse } from '../../core/api/discovery-api';
import { City, cityLabel, nearestCity } from '../../core/geo/cities';
import { LocationService, LocationSource } from '../../core/geo/location';
import { EmptyState } from '../../shared/empty-state/empty-state';
import { InlineMessage } from '../../shared/inline-message/inline-message';
import { PageHeader } from '../../shared/page-header/page-header';
import { DiscoverFilters } from './discover-filters';
import { DiscoverMap } from './discover-map';
import { EventCard } from './event-card';
import { FiltersSheet, FiltersSheetData } from './filters-sheet';
import { LocationField } from './location-field';
import {
  Coordinates,
  RADIUS_OPTIONS,
  SearchFilters,
  activeFilterCount,
  buildSearchQuery,
  coordinatesFromParams,
  filtersFromParams,
  roundCoordinate,
  toQueryParams,
} from './search-filters';

export const COMPACT_LAYOUT_QUERY = '(max-width: 1023.98px)';

type LocateState = 'idle' | 'locating' | 'failed';

@Component({
  selector: 'app-discover',
  imports: [
    MatBadge,
    MatButton,
    MatButtonToggle,
    MatButtonToggleGroup,
    MatIcon,
    MatProgressBar,
    DiscoverFilters,
    DiscoverMap,
    EventCard,
    EmptyState,
    InlineMessage,
    LocationField,
    PageHeader,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'app-page app-page--wide' },
  templateUrl: './discover.html',
  styleUrl: './discover.scss',
})
export class Discover {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly locationService = inject(LocationService);
  private readonly bottomSheet = inject(MatBottomSheet);
  private readonly document = inject(DOCUMENT);

  private readonly params = toSignal(this.route.queryParams, { initialValue: this.route.snapshot.queryParams });
  protected readonly filters = computed(() => filtersFromParams(this.params()));
  protected readonly where = computed(() => coordinatesFromParams(this.params()));
  protected readonly filterCount = computed(() => activeFilterCount(this.filters()));
  protected readonly compact = toSignal(
    inject(BreakpointObserver)
      .observe(COMPACT_LAYOUT_QUERY)
      .pipe(map((state) => state.matches)),
    { initialValue: false },
  );
  protected readonly view = signal<'list' | 'map'>('list');
  protected readonly highlightedId = signal<string | null>(null);
  protected readonly locateState = signal<LocateState>('idle');
  private readonly source = signal<LocationSource>('link');
  private readonly cityName = signal<string | null>(null);

  protected readonly locationLabel = computed(() => {
    const where = this.where();
    if (!where) {
      return '';
    }
    const near = nearestCity(where.lat, where.lng);
    const named = this.cityName() ?? (near ? cityLabel(near) : null);
    const place = named ? `Near ${named}` : `Near ${roundCoordinate(where.lat)}, ${roundCoordinate(where.lng)}`;
    return this.source() === 'network' ? `${place} (approximate)` : place;
  });

  protected readonly search = httpResource<SearchResponse>(() => {
    const where = this.where();
    return where
      ? { url: `${environment.apiBaseUrl}/search`, params: buildSearchQuery(where, this.filters(), new Date()) }
      : undefined;
  });
  protected readonly results = computed(() => (this.search.hasValue() ? this.search.value().events : []));
  protected readonly truncated = computed(() => this.search.hasValue() && this.search.value().truncated);
  protected readonly widerRadius = computed(() => RADIUS_OPTIONS.find((radius) => radius > this.filters().radius) ?? null);
  protected readonly resultSummary = computed(() => {
    const count = this.results().length;
    const noun = count === 1 ? 'game' : 'games';
    return `${count}${this.truncated() ? '+' : ''} ${noun} within ${this.filters().radius} mi`;
  });

  constructor() {
    // The sheet lives in an overlay, so a link inside it (the skill levels guide) would leave it open over the next page.
    inject(DestroyRef).onDestroy(() => this.bottomSheet.dismiss());
    if (!this.where()) {
      void this.locate();
    }
  }

  protected async locate(): Promise<void> {
    this.locateState.set('locating');
    const found = await this.locationService.locate();
    if (!found) {
      this.locateState.set('failed');
      return;
    }
    this.locateState.set('idle');
    this.setLocation(found, found.source, null);
  }

  protected chooseCity(city: City): void {
    this.locateState.set('idle');
    this.setLocation(city, 'manual', cityLabel(city));
  }

  protected updateFilters(filters: SearchFilters): void {
    void this.router.navigate([], { queryParams: toQueryParams(this.where(), filters), replaceUrl: true });
  }

  protected widenRadius(): void {
    const radius = this.widerRadius();
    if (radius) {
      this.updateFilters({ ...this.filters(), radius });
    }
  }

  protected openFilters(): void {
    this.bottomSheet.open<FiltersSheet, FiltersSheetData>(FiltersSheet, {
      data: { filters: this.filters(), apply: (filters) => this.updateFilters(filters) },
      ariaLabel: 'Filters',
    });
  }

  protected showOnList(publicId: string): void {
    this.highlightedId.set(publicId);
    this.document.getElementById(`result-${publicId}`)?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }

  private setLocation(where: Coordinates, source: LocationSource, name: string | null): void {
    this.source.set(source);
    this.cityName.set(name);
    void this.router.navigate([], { queryParams: toQueryParams(where, this.filters()), replaceUrl: true });
  }
}
