import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatAutocomplete, MatAutocompleteSelectedEvent, MatAutocompleteTrigger } from '@angular/material/autocomplete';
import { MatButton } from '@angular/material/button';
import { MatOptgroup, MatOption } from '@angular/material/core';
import { MatError, MatFormField, MatHint, MatLabel, MatPrefix, MatSuffix } from '@angular/material/form-field';
import { MatIcon } from '@angular/material/icon';
import { MatInput } from '@angular/material/input';
import { MatActionList, MatListItem, MatListItemIcon, MatListItemLine, MatListItemTitle } from '@angular/material/list';
import { MatProgressSpinner } from '@angular/material/progress-spinner';
import { Observable, Subject, catchError, debounceTime, distinctUntilChanged, map, of, switchMap, tap } from 'rxjs';
import { toApiProblem } from '../../../core/api/api-problem';
import {
  AddressSuggestion,
  AutocompleteVenuesResponse,
  Coordinates,
  SuggestionsStatus,
  Venue,
  VenuesApi,
} from '../../../core/api/venues-api';
import { InlineMessage } from '../../../shared/inline-message/inline-message';

export type VenuePickerOption =
  | { kind: 'venue'; venue: Venue }
  | { kind: 'suggestion'; suggestion: AddressSuggestion };

const FACILITY_NAME_MAX_LENGTH = 200;

export const AUTOCOMPLETE_DEBOUNCE_MS = 300;
const MIN_QUERY_LENGTH = 2;

const SUGGESTIONS_STATUS_MESSAGES: Partial<Record<SuggestionsStatus, string>> = {
  unavailable: 'New venues are temporarily unavailable. Pick a venue already on Hockey Index, or try again later.',
  limit_reached: 'You have reached today’s limit for new address lookups. Pick a venue already on Hockey Index, or try again tomorrow.',
};

const SEARCH_AGAIN_CODES: ReadonlySet<string> = new Set(['address_not_found', 'location_mismatch']);

let nextPickerId = 0;

@Component({
  selector: 'app-venue-picker',
  imports: [
    ReactiveFormsModule,
    MatActionList,
    MatAutocomplete,
    MatAutocompleteTrigger,
    MatButton,
    MatError,
    MatFormField,
    MatHint,
    MatIcon,
    MatInput,
    MatLabel,
    MatListItem,
    MatListItemIcon,
    MatListItemLine,
    MatListItemTitle,
    MatOptgroup,
    MatOption,
    MatPrefix,
    MatProgressSpinner,
    MatSuffix,
    InlineMessage,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './venue-picker.html',
  styleUrl: './venue-picker.scss',
})
export class VenuePicker {
  readonly near = input<Coordinates | null>(null);
  readonly venueSelected = output<Venue>();

  private readonly venuesApi = inject(VenuesApi);
  private readonly queries = new Subject<string>();

  protected readonly id = `venue-picker-${nextPickerId++}`;
  protected readonly query = signal('');
  protected readonly results = signal<AutocompleteVenuesResponse | null>(null);
  protected readonly searching = signal(false);
  protected readonly selected = signal<Venue | null>(null);
  protected readonly pendingSuggestion = signal<AddressSuggestion | null>(null);
  protected readonly facilityName = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(FACILITY_NAME_MAX_LENGTH)],
  });
  protected readonly facilityNameMaxLength = FACILITY_NAME_MAX_LENGTH;
  protected readonly candidates = signal<Venue[] | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.queries
      .pipe(
        map((query) => query.trim()),
        debounceTime(AUTOCOMPLETE_DEBOUNCE_MS),
        distinctUntilChanged(),
        tap(() => this.error.set(null)),
        switchMap((query) => (query.length < MIN_QUERY_LENGTH ? of(null) : this.search(query))),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((response) => {
        this.searching.set(false);
        this.results.set(response);
      });
  }

  protected suggestionsMessage(status: SuggestionsStatus): string | null {
    return SUGGESTIONS_STATUS_MESSAGES[status] ?? null;
  }

  protected onQuery(event: Event): void {
    const query = (event.target as HTMLInputElement).value;
    this.query.set(query);
    this.queries.next(query);
  }

  protected optionLabel(option: VenuePickerOption | string | null): string {
    if (!option || typeof option === 'string') {
      return option ?? '';
    }
    return option.kind === 'venue' ? option.venue.name : option.suggestion.label;
  }

  protected onOptionSelected(event: MatAutocompleteSelectedEvent): void {
    const option = event.option.value as VenuePickerOption;
    if (option.kind === 'venue') {
      this.chooseVenue(option.venue);
    } else {
      this.chooseSuggestion(option.suggestion);
    }
  }

  protected chooseVenue(venue: Venue): void {
    this.selected.set(venue);
    this.candidates.set(null);
    this.pendingSuggestion.set(null);
    this.error.set(null);
    this.venueSelected.emit(venue);
  }

  protected chooseSuggestion(suggestion: AddressSuggestion): void {
    this.pendingSuggestion.set(suggestion);
    this.facilityName.reset();
    this.error.set(null);
  }

  protected backToSearch(): void {
    this.pendingSuggestion.set(null);
    this.candidates.set(null);
    this.selected.set(null);
    this.error.set(null);
  }

  protected createVenue(confirmNew = false): void {
    const suggestion = this.pendingSuggestion();
    if (!suggestion) {
      return;
    }
    if (this.facilityName.invalid) {
      this.facilityName.markAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.venuesApi
      .create({
        name: this.facilityName.value.trim(),
        providerPlaceId: suggestion.providerPlaceId,
        address: suggestion.label,
        latitude: suggestion.latitude,
        longitude: suggestion.longitude,
        ...(confirmNew ? { confirmNew: true } : {}),
      })
      .subscribe({
        next: (venue) => {
          this.busy.set(false);
          this.chooseVenue(venue);
        },
        error: (error: unknown) => {
          this.busy.set(false);
          const problem = toApiProblem(error);
          if (problem.status === 409 && problem.code === 'venue_candidates') {
            this.candidates.set((problem.body['candidates'] as Venue[] | undefined) ?? []);
            return;
          }
          if (problem.code && SEARCH_AGAIN_CODES.has(problem.code)) {
            this.pendingSuggestion.set(null);
          }
          this.error.set(problem.title);
        },
      });
  }

  private search(query: string): Observable<AutocompleteVenuesResponse | null> {
    this.searching.set(true);
    return this.venuesApi.autocomplete(query, this.near()).pipe(
      catchError((error: unknown) => {
        this.error.set(toApiProblem(error).title);
        return of(null);
      }),
    );
  }
}
