import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatCard } from '@angular/material/card';
import { MatChip, MatChipSet } from '@angular/material/chips';
import { MatIcon } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { SearchResult } from '../../core/api/discovery-api';
import { VenueTimePipe, formatVenueTime } from '../../core/time/venue-time.pipe';
import { canonicalEventPath, feeLabel, skillLabel } from '../../../shared-render/event-summary';
import { StatusChip } from '../../shared/status-chip/status-chip';
import { typeLabel } from './search-filters';

function dayParts(instant: string, timeZone: string): { weekday: string; day: string; month: string } {
  const parts = new Intl.DateTimeFormat('en-US', { timeZone, weekday: 'short', day: 'numeric', month: 'short' }).formatToParts(
    new Date(instant),
  );
  const get = (type: Intl.DateTimeFormatPartTypes) => parts.find((part) => part.type === type)?.value ?? '';
  return { weekday: get('weekday'), day: get('day'), month: get('month') };
}

function formatDate(date: string): string {
  const [year, month, day] = date.split('-').map(Number);
  return new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' }).format(
    new Date(Date.UTC(year, month - 1, day)),
  );
}

/** One search result. The whole card is a link target through the title's stretched hit area. */
@Component({
  selector: 'app-event-card',
  imports: [MatCard, MatChipSet, MatChip, MatIcon, RouterLink, VenueTimePipe, StatusChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.cancelled]': "event().status === 'cancelled'" },
  template: `
    @let e = event();
    <mat-card appearance="outlined" class="card">
      <div class="date" aria-hidden="true">
        <span class="weekday">{{ day().weekday }}</span>
        <span class="day">{{ day().day }}</span>
        <span class="month">{{ day().month }}</span>
      </div>
      <div class="body">
        <div class="heading">
          <h3 class="title">
            <a class="stretched" [routerLink]="path()">{{ e.title }}</a>
          </h3>
          @if (e.status === 'cancelled') {
            <app-status-chip label="Cancelled" tone="critical" />
          }
        </div>
        <p class="when">
          <mat-icon aria-hidden="true">schedule</mat-icon>
          @if (e.startDate && e.endDate) {
            <span>{{ dateRange() }}</span>
          } @else {
            <span>{{ e.startsAt | venueTime: e.venue.timeZone : 'date' }}, {{ timeRange() }}</span>
          }
        </p>
        <p class="where">
          <mat-icon aria-hidden="true">location_on</mat-icon>
          <span>
            {{ e.venue.name }}@if (e.rinkLabel) {<span> · {{ e.rinkLabel }}</span>}, {{ e.venue.city }}
            <span class="distance">· {{ distance() }}</span>
          </span>
        </p>
        <mat-chip-set class="facts" aria-label="Details">
          <mat-chip [disableRipple]="true">{{ skill() }}</mat-chip>
          <mat-chip [disableRipple]="true">{{ fee() }}</mat-chip>
          <mat-chip [disableRipple]="true">{{ type() }}</mat-chip>
        </mat-chip-set>
      </div>
    </mat-card>
  `,
  styles: `
    :host {
      display: block;
    }

    .card {
      position: relative;
      display: flex;
      flex-direction: row;
      gap: 16px;
      padding: 16px;
      transition: background-color 150ms, border-color 150ms;
    }

    :host(:hover) .card,
    :host(:focus-within) .card,
    :host(.highlighted) .card {
      border-color: var(--mat-sys-primary);
      background: var(--mat-sys-surface-container-low);
    }

    .date {
      display: flex;
      flex: none;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      width: 64px;
      height: 76px;
      border-radius: var(--mat-sys-corner-medium);
      background: var(--mat-sys-primary-container);
      color: var(--mat-sys-on-primary-container);
      text-transform: uppercase;
    }

    :host(.cancelled) .date {
      background: var(--mat-sys-surface-container-highest);
      color: var(--mat-sys-on-surface-variant);
    }

    .weekday,
    .month {
      font: var(--mat-sys-label-small);
      letter-spacing: 0.08em;
    }

    .day {
      font: var(--mat-sys-headline-small);
      font-weight: 500;
      line-height: 1.1;
    }

    .body {
      display: flex;
      flex: 1 1 auto;
      flex-direction: column;
      gap: 4px;
      min-width: 0;
    }

    .heading {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 4px 8px;
    }

    .title {
      margin: 0;
      font: var(--mat-sys-title-medium);
      overflow-wrap: anywhere;
    }

    .stretched {
      color: var(--mat-sys-on-surface);
      text-decoration: none;
    }

    .stretched::after {
      content: '';
      position: absolute;
      inset: 0;
      border-radius: inherit;
    }

    .stretched:focus-visible {
      outline: none;
    }

    .stretched:focus-visible::after {
      outline: 3px solid var(--mat-sys-primary);
      outline-offset: -3px;
    }

    :host(.cancelled) .stretched {
      text-decoration: line-through;
      text-decoration-thickness: 1px;
    }

    .when,
    .where {
      display: flex;
      align-items: flex-start;
      gap: 6px;
      margin: 0;
      font: var(--mat-sys-body-medium);
      color: var(--mat-sys-on-surface-variant);
    }

    .when mat-icon,
    .where mat-icon {
      flex: none;
      width: 18px;
      height: 18px;
      margin-top: 1px;
      font-size: 18px;
    }

    .distance {
      white-space: nowrap;
    }

    .facts {
      margin-top: 4px;
      --mat-chip-container-height: 28px;
      --mat-chip-label-text-size: var(--mat-sys-label-medium-size);
    }

    @media (max-width: 479.98px) {
      .card {
        gap: 12px;
        padding: 12px;
      }

      .date {
        width: 52px;
        height: 64px;
      }

      .day {
        font: var(--mat-sys-title-large);
      }
    }
  `,
})
export class EventCard {
  readonly event = input.required<SearchResult>();

  protected readonly path = computed(() => canonicalEventPath(this.event().publicId, this.event().title));
  protected readonly day = computed(() => dayParts(this.event().startsAt, this.event().venue.timeZone));
  protected readonly skill = computed(() => skillLabel(this.event().skill));
  protected readonly fee = computed(() => {
    const label = feeLabel(this.event().feeCents, this.event().currency);
    return this.event().feeCents === null ? 'Fee varies' : label;
  });
  protected readonly type = computed(() => typeLabel(this.event().type));
  protected readonly distance = computed(() => {
    const miles = this.event().distanceMiles;
    return miles < 0.1 ? 'nearby' : `${miles < 10 ? miles.toFixed(1) : Math.round(miles)} mi`;
  });
  protected readonly timeRange = computed(() => {
    const { startsAt, endsAt, venue } = this.event();
    const start = formatVenueTime(startsAt, venue.timeZone, 'time').replace(/ [A-Z]{2,5}$/, '');
    return `${start} – ${formatVenueTime(endsAt, venue.timeZone, 'time')}`;
  });
  protected readonly dateRange = computed(() => {
    const { startDate, endDate } = this.event();
    return startDate && endDate ? `${formatDate(startDate)} – ${formatDate(endDate)}` : '';
  });
}
