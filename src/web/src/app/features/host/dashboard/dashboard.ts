import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatCard, MatCardContent, MatCardHeader, MatCardSubtitle, MatCardTitle } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIcon } from '@angular/material/icon';
import { MatMenu, MatMenuItem, MatMenuTrigger } from '@angular/material/menu';
import { MatProgressBar } from '@angular/material/progress-bar';
import { Router, RouterLink } from '@angular/router';
import { Observable, filter } from 'rxjs';
import { toApiProblem } from '../../../core/api/api-problem';
import { HostEvent, HostEventLimits, HostEventStatus, HostEventsApi } from '../../../core/api/host-events-api';
import { VenueTimePipe, formatVenueTime } from '../../../core/time/venue-time.pipe';
import { confirm } from '../../../shared/confirm-dialog/confirm-dialog';
import { EmptyState } from '../../../shared/empty-state/empty-state';
import { InlineMessage } from '../../../shared/inline-message/inline-message';
import { Notifier } from '../../../shared/notifier/notifier';
import { PageHeader } from '../../../shared/page-header/page-header';
import { StatusChip, StatusChipTone } from '../../../shared/status-chip/status-chip';
import { canonicalEventPath, feeLabel, skillLabel } from '../../../../shared-render/event-summary';
import { PUBLISH_PARKED_MESSAGE, blockedUrlsOf, problemMessage } from '../event-editor/event-problems';

const STATUS_CHIPS: Record<HostEventStatus, { label: string; tone: StatusChipTone }> = {
  draft: { label: 'Draft', tone: 'neutral' },
  published: { label: 'Published', tone: 'positive' },
  cancelled: { label: 'Cancelled', tone: 'critical' },
  archived: { label: 'Ended', tone: 'neutral' },
  hidden: { label: 'Hidden', tone: 'critical' },
};

const STATUS_ORDER: Record<HostEventStatus, number> = { draft: 0, published: 1, hidden: 2, cancelled: 3, archived: 4 };

interface ActionError {
  message: string;
  blockedUrls: string[];
  editEventId: string | null;
}

function formatLocalDate(isoDate: string): string {
  const [year, month, day] = isoDate.split('-').map(Number);
  return formatVenueTime(Date.UTC(year, month - 1, day), 'UTC', 'date');
}

@Component({
  selector: 'app-dashboard',
  imports: [
    RouterLink,
    MatButton,
    MatIconButton,
    MatCard,
    MatCardContent,
    MatCardHeader,
    MatCardSubtitle,
    MatCardTitle,
    MatIcon,
    MatMenu,
    MatMenuItem,
    MatMenuTrigger,
    MatProgressBar,
    EmptyState,
    InlineMessage,
    PageHeader,
    StatusChip,
    VenueTimePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'app-page app-page--wide' },
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class Dashboard {
  private readonly api = inject(HostEventsApi);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);
  private readonly router = inject(Router);

  protected readonly events = signal<HostEvent[] | null>(null);
  protected readonly limits = signal<HostEventLimits | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<ActionError | null>(null);
  protected readonly actionInfo = signal<string | null>(null);

  protected readonly sortedEvents = computed(() =>
    [...(this.events() ?? [])].sort(
      (a, b) => STATUS_ORDER[a.status] - STATUS_ORDER[b.status] || a.startsAt.localeCompare(b.startsAt),
    ),
  );

  protected readonly limitsText = computed(() => {
    const limits = this.limits();
    return limits
      ? `${limits.activeListings} of ${limits.maxActiveListings} active, ` +
          `${limits.publishesLast24Hours} of ${limits.maxPublishesPer24Hours} publishes today`
      : '';
  });

  protected readonly limitNotice = computed(() => {
    const limits = this.limits();
    if (!limits) {
      return null;
    }
    if (limits.activeListings >= limits.maxActiveListings) {
      return 'You have reached your limit of active events. Cancel an event or wait for one to end before you publish another.';
    }
    if (limits.publishesLast24Hours >= limits.maxPublishesPer24Hours) {
      return 'You have used all of today’s publishes. You can publish again tomorrow.';
    }
    return null;
  });

  protected readonly skillLabel = skillLabel;
  protected readonly feeLabel = feeLabel;

  constructor() {
    this.load();
  }

  protected status(event: HostEvent) {
    return STATUS_CHIPS[event.status];
  }

  protected dateRange(event: HostEvent): string {
    if (!event.startDate || !event.endDate) {
      return '';
    }
    return `${formatLocalDate(event.startDate)} – ${formatLocalDate(event.endDate)}`;
  }

  protected publicPath(event: HostEvent): string {
    return canonicalEventPath(event.publicId, event.title);
  }

  protected load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.api.list().subscribe({
      next: (response) => {
        this.loading.set(false);
        this.events.set(response.events);
        this.limits.set(response.limits);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(toApiProblem(error).title);
      },
    });
  }

  protected publish(event: HostEvent): void {
    this.run(event, this.api.publish(event.publicId), (result) => {
      if (result.parked) {
        this.actionInfo.set(`${event.title}: ${PUBLISH_PARKED_MESSAGE}`);
      } else {
        this.notifier.success(`Published ${event.title}.`);
      }
    });
  }

  protected duplicate(event: HostEvent): void {
    this.run(event, this.api.duplicate(event.publicId), (copy) => {
      this.notifier.success(`Created a draft copy of ${event.title}.`);
      void this.router.navigate(['/host/events', copy.publicId, 'edit']);
    });
  }

  protected cancel(event: HostEvent): void {
    this.confirmThen(
      { title: 'Cancel this event?', message: `${event.title} stays visible as cancelled. You cannot undo this.`, confirmLabel: 'Cancel event' },
      () => this.run(event, this.api.cancel(event.publicId), () => this.notifier.success(`Cancelled ${event.title}.`)),
    );
  }

  protected deleteDraft(event: HostEvent): void {
    this.confirmThen(
      { title: 'Delete this draft?', message: `${event.title} is deleted for good.`, confirmLabel: 'Delete draft' },
      () => this.run(event, this.api.delete(event.publicId), () => this.notifier.success(`Deleted ${event.title}.`)),
    );
  }

  private confirmThen(data: { title: string; message: string; confirmLabel: string }, action: () => void): void {
    confirm(this.dialog, { ...data, cancelLabel: 'Keep', destructive: true })
      .pipe(filter(Boolean))
      .subscribe(() => action());
  }

  private run<T>(event: HostEvent, request: Observable<T>, onSuccess: (result: T) => void): void {
    this.actionError.set(null);
    this.actionInfo.set(null);
    this.loading.set(true);
    request.subscribe({
      next: (result) => {
        onSuccess(result);
        this.load();
      },
      error: (error: unknown) => {
        const problem = toApiProblem(error);
        const blocked = problem.code === 'join_instructions_blocked';
        this.actionError.set({
          message: `${event.title}: ${problemMessage(problem)}`,
          blockedUrls: blocked ? blockedUrlsOf(problem) : [],
          editEventId: blocked ? event.publicId : null,
        });
        this.load();
      },
    });
  }
}
