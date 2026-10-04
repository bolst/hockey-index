import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { AddressSuggestion, AutocompleteVenuesResponse, Venue } from '../../../core/api/venues-api';
import { AUTOCOMPLETE_DEBOUNCE_MS, VenuePicker } from './venue-picker';

const api = environment.apiBaseUrl;

function venue(publicId: string, name: string): Venue {
  return {
    publicId,
    name,
    addressLine: '50 Carlton St',
    city: 'Toronto',
    region: 'ON',
    country: 'CA',
    latitude: 43.6619,
    longitude: -79.3802,
    timeZone: 'America/Toronto',
    distanceMeters: null,
  };
}

const suggestion: AddressSuggestion = {
  providerPlaceId: 'fake.mapbox.toronto-carlton',
  label: '50 Carlton Street, Toronto, Ontario M5B 1J2, Canada',
  addressLine: '50 Carlton Street',
  city: 'Toronto',
  region: 'Ontario',
  country: 'Canada',
  latitude: 43.6619,
  longitude: -79.3802,
};

describe('VenuePicker', () => {
  let fixture: ComponentFixture<VenuePicker>;
  let controller: HttpTestingController;
  let selected: Venue[];

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(VenuePicker);
    selected = [];
    fixture.componentInstance.venueSelected.subscribe((value) => selected.push(value));
    fixture.detectChanges();
  });

  afterEach(() => {
    controller.verify();
    vi.useRealTimers();
  });

  const element = () => fixture.nativeElement as HTMLElement;

  function type(text: string): void {
    const input = element().querySelector<HTMLInputElement>('input[type="search"]')!;
    input.focus();
    input.value = text;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  /** Autocomplete options render in the CDK overlay, outside the fixture element. */
  function optionGroup(testId: string): HTMLElement | null {
    return document.querySelector<HTMLElement>(`mat-optgroup[data-testid="${testId}"]`);
  }

  function chooseOption(text: string): void {
    const option = Array.from(document.querySelectorAll<HTMLElement>('mat-option')).find((o) =>
      o.textContent?.includes(text),
    );
    if (!option) {
      throw new Error(`No autocomplete option with text "${text}"`);
    }
    option.click();
    fixture.detectChanges();
  }

  function respond(query: string, response: AutocompleteVenuesResponse): void {
    vi.advanceTimersByTime(AUTOCOMPLETE_DEBOUNCE_MS);
    controller
      .expectOne((request) => request.url === `${api}/venues/autocomplete` && request.params.get('q') === query)
      .flush(response);
    fixture.detectChanges();
  }

  function clickButton(text: string): void {
    const button = Array.from(element().querySelectorAll('button')).find((b) => b.textContent?.includes(text));
    if (!button) {
      throw new Error(`No button with text "${text}"`);
    }
    button.click();
    fixture.detectChanges();
  }

  it('debounces typing into one autocomplete request', () => {
    type('ma');
    vi.advanceTimersByTime(100);
    type('mat');
    vi.advanceTimersByTime(100);
    type('matt');
    controller.expectNone(`${api}/venues/autocomplete`);
    respond('matt', { venues: [], suggestions: [], suggestionsStatus: 'ok' });
  });

  it('does not search for fewer than 2 characters', () => {
    type('m');
    vi.advanceTimersByTime(AUTOCOMPLETE_DEBOUNCE_MS);
    controller.expectNone(() => true);
  });

  it('lists database venues before provider suggestions and selects a venue directly', () => {
    type('carlton');
    respond('carlton', {
      venues: [venue('a1b2c3d4e5', 'Mattamy Athletic Centre')],
      suggestions: [suggestion],
      suggestionsStatus: 'ok',
    });

    const db = optionGroup('db-venues')!;
    const suggestions = optionGroup('suggestions')!;
    expect(db.getAttribute('role')).toBe('group');
    expect(db.compareDocumentPosition(suggestions) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(db.textContent).toContain('Mattamy Athletic Centre');
    expect(suggestions.textContent).toContain(suggestion.label);

    chooseOption('Mattamy Athletic Centre');
    expect(selected.map((v) => v.publicId)).toEqual(['a1b2c3d4e5']);
    expect(element().textContent).toContain('Mattamy Athletic Centre');
    controller.expectNone(`${api}/venues`);
  });

  it.each([
    ['unavailable', 'New venues are temporarily unavailable'],
    ['limit_reached', 'today’s limit'],
  ] as const)('explains suggestionsStatus %s', (status, message) => {
    type('rink');
    respond('rink', { venues: [venue('a1b2c3d4e5', 'Rink')], suggestions: [], suggestionsStatus: status });
    expect(element().querySelector('[data-testid="suggestions-status"]')?.textContent).toContain(message);
  });

  it('shows no status message when suggestions were not needed', () => {
    type('rink');
    respond('rink', { venues: [venue('a1b2c3d4e5', 'Rink')], suggestions: [], suggestionsStatus: 'not_needed' });
    expect(element().querySelector('[data-testid="suggestions-status"]')).toBeNull();
  });

  function startCreate(name: string): void {
    type('50 carlton');
    respond('50 carlton', { venues: [], suggestions: [suggestion], suggestionsStatus: 'ok' });
    chooseOption(suggestion.label);
    const nameInput = element().querySelector<HTMLInputElement>('input:not([type="search"])')!;
    nameInput.value = name;
    nameInput.dispatchEvent(new Event('input'));
    clickButton('Add venue');
  }

  it('creates a venue from a suggestion', () => {
    startCreate('New Rink');
    const request = controller.expectOne(`${api}/venues`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      name: 'New Rink',
      providerPlaceId: suggestion.providerPlaceId,
      address: suggestion.label,
      latitude: suggestion.latitude,
      longitude: suggestion.longitude,
    });
    request.flush(venue('newvenue01', 'New Rink'), { status: 201, statusText: 'Created' });
    fixture.detectChanges();
    expect(selected.map((v) => v.publicId)).toEqual(['newvenue01']);
  });

  it('offers "Did you mean…?" on 409 and can pick an existing candidate', () => {
    startCreate('Mattamy');
    controller.expectOne(`${api}/venues`).flush(
      { status: 409, title: 'Did you mean one of these venues?', code: 'venue_candidates', candidates: [venue('a1b2c3d4e5', 'Mattamy Athletic Centre')] },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();

    expect(element().textContent).toContain('Did you mean…?');
    clickButton('Mattamy Athletic Centre');
    expect(selected.map((v) => v.publicId)).toEqual(['a1b2c3d4e5']);
  });

  it('retries with confirmNew when the host rejects the candidates', () => {
    startCreate('Brand New Arena');
    controller.expectOne(`${api}/venues`).flush(
      { status: 409, code: 'venue_candidates', candidates: [venue('a1b2c3d4e5', 'Mattamy Athletic Centre')] },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();

    clickButton('None of these');
    const retry = controller.expectOne(`${api}/venues`);
    expect(retry.request.body).toEqual(expect.objectContaining({ name: 'Brand New Arena', confirmNew: true }));
    retry.flush(venue('newvenue02', 'Brand New Arena'), { status: 201, statusText: 'Created' });
    fixture.detectChanges();
    expect(selected.map((v) => v.publicId)).toEqual(['newvenue02']);
  });

  it.each([
    [422, 'invalid_venue', 'Name must be 1–200 characters.'],
    [429, 'venue_limit_reached', 'You have added too many venues today. Try again tomorrow.'],
    [503, 'venue_provider_unavailable', 'New venues are temporarily unavailable.'],
  ])('shows the API message for %s %s', (status, code, title) => {
    startCreate('X');
    controller.expectOne(`${api}/venues`).flush({ status, code, title }, { status, statusText: 'Error' });
    fixture.detectChanges();
    expect(element().querySelector('[role="alert"]')?.textContent).toContain(title);
    expect(selected).toEqual([]);
  });

  it('returns to search when the address cannot be confirmed', () => {
    startCreate('X');
    controller
      .expectOne(`${api}/venues`)
      .flush(
        { status: 422, code: 'address_not_found', title: 'The selected address could not be confirmed. Search again.' },
        { status: 422, statusText: 'Unprocessable' },
      );
    fixture.detectChanges();
    expect(element().querySelector('input[type="search"]')).not.toBeNull();
    expect(element().querySelector('[role="alert"]')?.textContent).toContain('Search again');
  });
});
