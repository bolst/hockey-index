import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatButtonToggle, MatButtonToggleGroup } from '@angular/material/button-toggle';
import { MatCard, MatCardContent, MatCardHeader, MatCardTitle } from '@angular/material/card';
import { MatOption, provideNativeDateAdapter } from '@angular/material/core';
import {
  MatDatepicker,
  MatDatepickerInput,
  MatDatepickerToggle,
  MatDateRangeInput,
  MatDateRangePicker,
  MatEndDate,
  MatStartDate,
} from '@angular/material/datepicker';
import { MatError, MatFormField, MatHint, MatLabel, MatPrefix, MatSuffix } from '@angular/material/form-field';
import { MatIcon } from '@angular/material/icon';
import { MatInput } from '@angular/material/input';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatRadioButton, MatRadioGroup } from '@angular/material/radio';
import { MatSelect } from '@angular/material/select';
import { MatSlider, MatSliderRangeThumb } from '@angular/material/slider';
import { MatTimepicker, MatTimepickerInput, MatTimepickerToggle } from '@angular/material/timepicker';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom, map } from 'rxjs';
import { ApiProblem, toApiProblem } from '../../../core/api/api-problem';
import { EventType } from '../../../core/api/discovery-api';
import { Currency, EventRequest, HostEvent, HostEventsApi } from '../../../core/api/host-events-api';
import { InlineMessage } from '../../../shared/inline-message/inline-message';
import { LinkifyText } from '../../../shared/linkify-text/linkify-text';
import { Notifier } from '../../../shared/notifier/notifier';
import { PageHeader } from '../../../shared/page-header/page-header';
import { SkillLevelsLink } from '../../../shared/skill-levels-link/skill-levels-link';
import { VenuePicker } from '../venue-picker/venue-picker';
import { skillLabel } from '../../../../shared-render/event-summary';
import { EDIT_PARKED_MESSAGE, PUBLISH_PARKED_MESSAGE, blockedUrlsOf, problemMessage } from './event-problems';

export type FeeMode = 'free' | 'fixed' | 'see';
type SaveMode = 'draft' | 'publish' | 'changes';

interface EditorVenue {
  publicId: string;
  name: string;
  addressLine: string;
  city: string;
  country: string;
}

export const TITLE_MAX = 100;
export const DESCRIPTION_MAX = 2000;
export const JOIN_MAX = 2000;
export const SCHEDULE_MAX = 500;
export const RINK_MAX = 40;
const PREVIEW_EVENT_ID = 'aaaaaaaaaa';
const SKIPPED_TIME_MESSAGE = 'That time does not exist on that day (clocks change).';

const pad = (value: number) => String(value).padStart(2, '0');

function dateString(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function timeString(time: Date): string {
  return `${pad(time.getHours())}:${pad(time.getMinutes())}`;
}

function minutesOf(time: Date): number {
  return time.getHours() * 60 + time.getMinutes();
}

/** Parses a naive `YYYY-MM-DD[THH:mm[:ss]]` string into a local Date with the same wall-clock parts. */
function parseLocal(value: string): Date {
  const [year, month, day, hours = 0, minutes = 0] = value.split(/[-T:]/).map(Number);
  return new Date(year, month - 1, day, hours, minutes);
}

function nullIfBlank(value: string): string | null {
  const trimmed = value.trim();
  return trimmed ? trimmed : null;
}

function defaultCurrency(country: string): Currency {
  return country === 'US' ? 'USD' : 'CAD';
}

@Component({
  selector: 'app-event-editor',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatButton,
    MatButtonToggle,
    MatButtonToggleGroup,
    MatCard,
    MatCardContent,
    MatCardHeader,
    MatCardTitle,
    MatDatepicker,
    MatDatepickerInput,
    MatDatepickerToggle,
    MatDateRangeInput,
    MatDateRangePicker,
    MatEndDate,
    MatError,
    MatFormField,
    MatHint,
    MatIcon,
    MatInput,
    MatLabel,
    MatOption,
    MatPrefix,
    MatProgressBar,
    MatRadioButton,
    MatRadioGroup,
    MatSelect,
    MatSlider,
    MatSliderRangeThumb,
    MatStartDate,
    MatSuffix,
    MatTimepicker,
    MatTimepickerInput,
    MatTimepickerToggle,
    InlineMessage,
    LinkifyText,
    PageHeader,
    SkillLevelsLink,
    VenuePicker,
  ],
  providers: [provideNativeDateAdapter()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'app-page' },
  templateUrl: './event-editor.html',
  styleUrl: './event-editor.scss',
})
export class EventEditor {
  private readonly api = inject(HostEventsApi);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly notifier = inject(Notifier);
  private readonly eventId = inject(ActivatedRoute).snapshot.paramMap.get('id');

  protected readonly limits = { TITLE_MAX, DESCRIPTION_MAX, JOIN_MAX, SCHEDULE_MAX, RINK_MAX };

  protected readonly form = new FormGroup({
    type: new FormControl<EventType>('scrimmage', { nonNullable: true }),
    date: new FormControl<Date | null>(null, Validators.required),
    startTime: new FormControl<Date | null>(null, Validators.required),
    endTime: new FormControl<Date | null>(null, Validators.required),
    startDate: new FormControl<Date | null>(null, Validators.required),
    endDate: new FormControl<Date | null>(null, Validators.required),
    scheduleText: new FormControl('', { nonNullable: true, validators: Validators.maxLength(SCHEDULE_MAX) }),
    rinkLabel: new FormControl('', { nonNullable: true, validators: Validators.maxLength(RINK_MAX) }),
    title: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(TITLE_MAX)],
    }),
    description: new FormControl('', { nonNullable: true, validators: Validators.maxLength(DESCRIPTION_MAX) }),
    skillMin: new FormControl(0, { nonNullable: true }),
    skillMax: new FormControl(7, { nonNullable: true }),
    feeMode: new FormControl<FeeMode>('free', { nonNullable: true }),
    feeAmount: new FormControl<number | null>(null, [Validators.required, Validators.min(0.01)]),
    currency: new FormControl<Currency>('CAD', { nonNullable: true }),
    joinInstructions: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(JOIN_MAX)],
    }),
  });

  protected readonly value = toSignal(this.form.valueChanges.pipe(map(() => this.form.getRawValue())), {
    initialValue: this.form.getRawValue(),
  });

  protected readonly event = signal<HostEvent | null>(null);
  protected readonly venue = signal<EditorVenue | null>(null);
  protected readonly changingVenue = signal(false);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly fieldErrorSummary = signal<string[]>([]);
  protected readonly venueError = signal<string | null>(null);
  protected readonly stale = signal(false);
  protected readonly info = signal<string | null>(null);
  protected readonly blockedUrls = signal<string[]>([]);
  protected readonly tooManyUrls = signal(false);

  protected readonly isNew = this.eventId === null;
  protected readonly isDraft = computed(() => (this.event()?.status ?? 'draft') === 'draft');
  protected readonly isScrimmage = computed(() => this.value().type === 'scrimmage');
  protected readonly skillText = computed(() => skillLabel({ min: this.value().skillMin, max: this.value().skillMax }));
  protected readonly previewEventId = computed(() => this.event()?.publicId ?? PREVIEW_EVENT_ID);
  protected readonly endsNextDay = computed(() => {
    const { startTime, endTime } = this.value();
    return this.isScrimmage() && !!startTime && !!endTime && minutesOf(endTime) <= minutesOf(startTime);
  });

  constructor() {
    this.form.controls.type.valueChanges.subscribe(() => this.syncControls());
    this.form.controls.feeMode.valueChanges.subscribe(() => this.syncControls());
    this.syncControls();
    if (this.eventId) {
      this.load(this.eventId);
    }
  }

  protected errorText(control: AbstractControl, fallback: string): string {
    return (control.getError('server') as string | null) ?? fallback;
  }

  protected onVenueSelected(venue: EditorVenue): void {
    this.venue.set(venue);
    this.changingVenue.set(false);
    this.venueError.set(null);
    if (!this.form.controls.currency.dirty) {
      this.form.controls.currency.setValue(defaultCurrency(venue.country));
    }
  }

  protected changeVenue(): void {
    this.changingVenue.set(true);
  }

  protected reload(): void {
    const id = this.event()?.publicId ?? this.eventId;
    if (id) {
      this.load(id);
    }
  }

  protected async save(mode: SaveMode): Promise<void> {
    this.clearMessages();
    if (!this.validate()) {
      return;
    }
    const request = this.buildRequest();
    this.saving.set(true);
    this.form.disable({ emitEvent: false });
    try {
      await this.persist(mode, request);
    } catch (error) {
      this.endSaving();
      this.handleProblem(toApiProblem(error));
      return;
    }
    this.endSaving();
  }

  private async persist(mode: SaveMode, request: EventRequest): Promise<void> {
    const existing = this.event();
    let saved: HostEvent;
    let parked = false;
    if (existing) {
      const result = await firstValueFrom(this.api.update(existing.publicId, request, existing.version));
      saved = result.event;
      parked = result.parked;
      this.event.set(saved);
      if (parked) {
        this.info.set(EDIT_PARKED_MESSAGE);
      }
    } else {
      saved = await firstValueFrom(this.api.create(request));
      this.event.set(saved);
      if (mode === 'draft') {
        this.notifier.success('Draft saved.');
        await this.router.navigate(['/host/events', saved.publicId, 'edit'], { replaceUrl: true });
        return;
      }
      this.location.replaceState(`/host/events/${saved.publicId}/edit`);
    }

    if (mode === 'publish') {
      const result = await firstValueFrom(this.api.publish(saved.publicId));
      this.event.set(result.event);
      if (result.parked) {
        this.info.set(PUBLISH_PARKED_MESSAGE);
        return;
      }
      this.notifier.success(`Published ${result.event.title}.`);
      await this.router.navigateByUrl('/host');
      return;
    }
    if (!parked) {
      this.notifier.success(mode === 'draft' ? 'Draft saved.' : 'Changes saved.');
    }
  }

  private load(id: string): void {
    this.loading.set(true);
    this.clearMessages();
    this.api.get(id).subscribe({
      next: (event) => {
        this.loading.set(false);
        this.resetFrom(event);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.error.set(problemMessage(toApiProblem(error)));
      },
    });
  }

  private resetFrom(event: HostEvent): void {
    this.event.set(event);
    this.venue.set(event.venue);
    this.changingVenue.set(false);
    const starts = parseLocal(event.startsLocal);
    const fixedFee = event.feeCents !== null && event.feeCents > 0;
    this.form.reset({
      type: event.type,
      date: new Date(starts.getFullYear(), starts.getMonth(), starts.getDate()),
      startTime: starts,
      endTime: parseLocal(event.endsLocal),
      startDate: event.startDate ? parseLocal(event.startDate) : null,
      endDate: event.endDate ? parseLocal(event.endDate) : null,
      scheduleText: event.scheduleText ?? '',
      rinkLabel: event.rinkLabel ?? '',
      title: event.title,
      description: event.description ?? '',
      skillMin: event.skill.min,
      skillMax: event.skill.max,
      feeMode: event.feeCents === null ? 'see' : fixedFee ? 'fixed' : 'free',
      feeAmount: fixedFee ? event.feeCents! / 100 : null,
      currency: event.currency,
      joinInstructions: event.pendingJoinInstructions ?? event.joinInstructions,
    });
    this.form.controls.currency.markAsDirty();
    this.syncControls();
  }

  private syncControls(): void {
    const { type, feeMode } = this.form.getRawValue();
    const scrimmage = type === 'scrimmage';
    const controls = this.form.controls;
    const toggle = (control: AbstractControl, enabled: boolean) =>
      enabled ? control.enable({ emitEvent: false }) : control.disable({ emitEvent: false });
    [controls.date, controls.startTime, controls.endTime].forEach((control) => toggle(control, scrimmage));
    [controls.startDate, controls.endDate, controls.scheduleText].forEach((control) => toggle(control, !scrimmage));
    toggle(controls.feeAmount, feeMode === 'fixed');
  }

  private endSaving(): void {
    this.form.enable({ emitEvent: false });
    this.syncControls();
    this.saving.set(false);
  }

  private clearMessages(): void {
    this.error.set(null);
    this.fieldErrorSummary.set([]);
    this.venueError.set(null);
    this.stale.set(false);
    this.info.set(null);
    this.blockedUrls.set([]);
    this.tooManyUrls.set(false);
  }

  private validate(): boolean {
    const venueMissing = !this.venue() || this.changingVenue();
    if (venueMissing) {
      this.venueError.set('Choose a venue.');
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
    }
    return !venueMissing && this.form.valid;
  }

  private buildRequest(): EventRequest {
    const value = this.form.getRawValue();
    const scrimmage = value.type === 'scrimmage';
    let startsLocal: string | null = null;
    let endsLocal: string | null = null;
    if (scrimmage && value.date && value.startTime && value.endTime) {
      const date = value.date;
      const endDate = this.endsNextDay() ? new Date(date.getFullYear(), date.getMonth(), date.getDate() + 1) : date;
      startsLocal = `${dateString(date)}T${timeString(value.startTime)}:00`;
      endsLocal = `${dateString(endDate)}T${timeString(value.endTime)}:00`;
    }
    return {
      venueId: this.venue()!.publicId,
      rinkLabel: nullIfBlank(value.rinkLabel),
      type: value.type,
      title: value.title.trim(),
      description: nullIfBlank(value.description),
      startsLocal,
      endsLocal,
      startDate: !scrimmage && value.startDate ? dateString(value.startDate) : null,
      endDate: !scrimmage && value.endDate ? dateString(value.endDate) : null,
      scheduleText: scrimmage ? null : nullIfBlank(value.scheduleText),
      skill: { min: value.skillMin, max: value.skillMax },
      feeCents: value.feeMode === 'free' ? 0 : value.feeMode === 'fixed' ? Math.round((value.feeAmount ?? 0) * 100) : null,
      currency: value.currency,
      joinInstructions: value.joinInstructions.trim(),
    };
  }

  private handleProblem(problem: ApiProblem): void {
    const controls = this.form.controls;
    switch (problem.code) {
      case 'event_stale':
      case 'event_changed':
        this.stale.set(true);
        return;
      case 'join_instructions_blocked':
        this.blockedUrls.set(blockedUrlsOf(problem));
        this.tooManyUrls.set(problem.body['tooManyUrls'] === true);
        this.setServerError(controls.joinInstructions, problem.title);
        return;
      case 'invalid_event':
        this.applyFieldErrors(problem);
        return;
      case 'local_time_skipped': {
        const field = problem.body['field'] === 'endsLocal' ? 'end' : 'start';
        const scrimmage = this.isScrimmage();
        const control =
          field === 'end' ? (scrimmage ? controls.endTime : controls.endDate) : scrimmage ? controls.startTime : controls.startDate;
        this.setServerError(control, SKIPPED_TIME_MESSAGE);
        this.error.set(SKIPPED_TIME_MESSAGE);
        return;
      }
      case 'venue_not_found':
        this.venueError.set(problemMessage(problem));
        return;
      default:
        this.error.set(problemMessage(problem));
    }
  }

  private applyFieldErrors(problem: ApiProblem): void {
    const errors = (problem.body['errors'] ?? {}) as Record<string, string>;
    const controls = this.form.controls;
    const scrimmage = this.isScrimmage();
    const fieldControls: Record<string, AbstractControl> = {
      title: controls.title,
      description: controls.description,
      rinkLabel: controls.rinkLabel,
      scheduleText: controls.scheduleText,
      joinInstructions: controls.joinInstructions,
      feeCents: controls.feeAmount,
      startsLocal: scrimmage ? controls.startTime : controls.startDate,
      endsLocal: scrimmage ? controls.endTime : controls.endDate,
      startDate: controls.startDate,
      endDate: controls.endDate,
    };
    const unmapped: string[] = [];
    for (const [field, message] of Object.entries(errors)) {
      const control = fieldControls[field.charAt(0).toLowerCase() + field.slice(1)];
      if (control?.enabled) {
        this.setServerError(control, message);
      } else {
        unmapped.push(message);
      }
    }
    this.error.set(problem.title);
    this.fieldErrorSummary.set(unmapped);
  }

  private setServerError(control: AbstractControl, message: string): void {
    control.setErrors({ ...control.errors, server: message });
    control.markAsTouched();
  }
}
