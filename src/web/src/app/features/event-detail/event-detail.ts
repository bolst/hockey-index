import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButton } from '@angular/material/button';
import { MatCard, MatCardContent, MatCardHeader, MatCardTitle } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { environment } from '../../../environments/environment';
import { formatVenueTime } from '../../core/time/venue-time.pipe';
import {
  PUBLIC_ID_PATTERN,
  PublicEvent,
  SITE_NAME,
  canonicalEventPath,
  eventSlug,
  feeLabel,
  skillLabel,
} from '../../../shared-render/event-summary';
import { EmptyState } from '../../shared/empty-state/empty-state';
import { InlineMessage } from '../../shared/inline-message/inline-message';
import { LinkifyText } from '../../shared/linkify-text/linkify-text';
import { SkillLevelsLink } from '../../shared/skill-levels-link/skill-levels-link';
import { StatusChip } from '../../shared/status-chip/status-chip';
import { openReportDialog } from '../report/report-dialog';

const TYPE_LABELS: Record<string, string> = { scrimmage: 'Scrimmage', league: 'League', tournament: 'Tournament' };

function formatIsoDate(date: string): string {
  const [year, month, day] = date.slice(0, 10).split('-').map(Number);
  return new Intl.DateTimeFormat('en-US', { timeZone: 'UTC', weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' }).format(
    new Date(Date.UTC(year, month - 1, day)),
  );
}

function endOfIsoDateUtc(date: string): number {
  const [year, month, day] = date.slice(0, 10).split('-').map(Number);
  return Date.UTC(year, month - 1, day + 1, 12);
}

/**
 * Public event page. The Pages Function serves an escaped text summary for crawlers and no-JS
 * readers; this component replaces it with the live event from the API. All event text renders
 * as text; join instruction links go through /out.
 */
@Component({
  selector: 'app-event-detail',
  imports: [
    MatButton,
    MatCard,
    MatCardContent,
    MatCardHeader,
    MatCardTitle,
    MatIcon,
    MatProgressBar,
    RouterLink,
    EmptyState,
    InlineMessage,
    LinkifyText,
    SkillLevelsLink,
    StatusChip,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'app-page' },
  templateUrl: './event-detail.html',
  styleUrl: './event-detail.scss',
})
export class EventDetail {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly title = inject(Title);
  private readonly dialog = inject(MatDialog);

  private readonly params = toSignal(this.route.paramMap, { initialValue: this.route.snapshot.paramMap });
  protected readonly publicId = computed(() => this.params().get('id') ?? '');
  private readonly validId = computed(() => PUBLIC_ID_PATTERN.test(this.publicId()));

  protected readonly resource = httpResource<PublicEvent>(() =>
    this.validId() ? `${environment.apiBaseUrl}/events/${encodeURIComponent(this.publicId())}` : undefined,
  );
  protected readonly event = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
  protected readonly notFound = computed(() => {
    const error = this.resource.error();
    return !this.validId() || (error instanceof HttpErrorResponse && error.status === 404);
  });
  protected readonly failed = computed(() => !!this.resource.error() && !this.notFound());

  protected readonly ended = computed(() => {
    const event = this.event();
    if (!event) {
      return false;
    }
    const end = event.endDate ? endOfIsoDateUtc(event.endDate) : Date.parse(event.endsAt);
    return event.status === 'archived' || end < Date.now();
  });
  protected readonly cancelled = computed(() => this.event()?.status === 'cancelled');
  protected readonly typeLabel = computed(() => TYPE_LABELS[this.event()?.type ?? ''] ?? 'Event');
  protected readonly skill = computed(() => {
    const event = this.event();
    return event ? skillLabel(event.skill) : '';
  });
  protected readonly fee = computed(() => {
    const event = this.event();
    return event ? feeLabel(event.feeCents, event.currency) : '';
  });
  protected readonly dateLine = computed(() => {
    const event = this.event();
    if (!event) {
      return '';
    }
    if (event.startDate && event.endDate) {
      return `${formatIsoDate(event.startDate)} – ${formatIsoDate(event.endDate)}`;
    }
    return formatVenueTime(event.startsAt, event.venue.timeZone, 'date');
  });
  protected readonly timeLine = computed(() => {
    const event = this.event();
    if (!event) {
      return '';
    }
    if (event.startDate && event.endDate) {
      return event.scheduleText ?? '';
    }
    const start = formatVenueTime(event.startsAt, event.venue.timeZone, 'time').replace(/ [A-Z]{2,5}$/, '');
    return `${start} – ${formatVenueTime(event.endsAt, event.venue.timeZone, 'time')}`;
  });
  protected readonly otherTimeZone = computed(() => {
    const event = this.event();
    const viewerZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
    return event && event.venue.timeZone !== viewerZone ? event.venue.timeZone.replace(/_/g, ' ') : null;
  });
  protected readonly cancelledOn = computed(() => {
    const event = this.event();
    return event?.cancelledAt ? formatVenueTime(event.cancelledAt, event.venue.timeZone, 'date') : null;
  });

  constructor() {
    effect(() => {
      const event = this.event();
      if (!event) {
        return;
      }
      this.title.setTitle(`${event.title} · ${SITE_NAME}`);
      const slug = this.params().get('slug') ?? '';
      if (slug !== eventSlug(event.title)) {
        void this.router.navigateByUrl(canonicalEventPath(event.publicId, event.title), { replaceUrl: true });
      }
    });
  }

  protected report(): void {
    const event = this.event();
    if (event) {
      openReportDialog(this.dialog, { publicId: event.publicId, title: event.title });
    }
  }
}
