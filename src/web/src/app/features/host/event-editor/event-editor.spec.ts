import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { EventRequest, HostEvent } from '../../../core/api/host-events-api';
import { Notifier } from '../../../shared/notifier/notifier';
import { EventEditor } from './event-editor';

const api = `${environment.apiBaseUrl}/host/events`;

function hostEvent(overrides: Partial<HostEvent> = {}): HostEvent {
  return {
    publicId: 'evt0000001',
    status: 'draft',
    type: 'scrimmage',
    title: 'Friday skate',
    description: null,
    venue: {
      publicId: 'venue00001',
      name: 'Mattamy Athletic Centre',
      addressLine: '50 Carlton St',
      city: 'Toronto',
      region: 'ON',
      country: 'CA',
      timeZone: 'America/Toronto',
    },
    rinkLabel: null,
    startsLocal: '2026-11-14T21:00:00',
    endsLocal: '2026-11-14T22:30:00',
    startsAt: '2026-11-15T02:00:00Z',
    endsAt: '2026-11-15T03:30:00Z',
    startDate: null,
    endDate: null,
    scheduleText: null,
    skill: { min: 2, max: 4 },
    feeCents: 0,
    currency: 'CAD',
    joinInstructions: 'Email me at host@example.com',
    pendingJoinInstructions: null,
    publishRequestedAt: null,
    hiddenReason: null,
    notice: null,
    publishedAt: null,
    cancelledAt: null,
    archivedAt: null,
    createdAt: '2026-10-01T00:00:00Z',
    updatedAt: '2026-10-01T00:00:00Z',
    version: 7,
    resolvedAmbiguousTimes: [],
    ...overrides,
  };
}

describe('EventEditor', () => {
  let fixture: ComponentFixture<EventEditor>;
  let controller: HttpTestingController;
  let success: ReturnType<typeof vi.fn>;

  const element = () => fixture.nativeElement as HTMLElement;

  function setUp(id: string | null): void {
    success = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Notifier, useValue: { success, error: vi.fn() } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}) } } },
      ],
    });
    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EventEditor);
    fixture.detectChanges();
  }

  function loadExisting(event = hostEvent()): void {
    setUp(event.publicId);
    controller.expectOne(`${api}/${event.publicId}`).flush(event);
    fixture.detectChanges();
  }

  afterEach(() => {
    controller.verify();
    vi.restoreAllMocks();
  });

  function field(label: string): HTMLInputElement | HTMLTextAreaElement {
    const labelElement = Array.from(element().querySelectorAll('label')).find(
      (candidate) => candidate.textContent?.trim() === label,
    );
    const control = labelElement
      ? (document.getElementById(labelElement.getAttribute('for')!) as HTMLInputElement | null)
      : element().querySelector<HTMLInputElement>(`[aria-label="${label}"]`);
    if (!control) throw new Error(`No field labelled "${label}"`);
    return control;
  }

  function hasField(label: string): boolean {
    try {
      field(label);
      return true;
    } catch {
      return false;
    }
  }

  function type(label: string, value: string): void {
    const control = field(label);
    control.value = value;
    control.dispatchEvent(new Event('input'));
    control.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
  }

  function button(name: string): HTMLButtonElement {
    const match = Array.from(element().querySelectorAll<HTMLButtonElement>('button')).find(
      (candidate) => candidate.textContent?.trim() === name,
    );
    if (!match) throw new Error(`No button "${name}"`);
    return match;
  }

  function click(name: string): void {
    button(name).click();
    fixture.detectChanges();
  }

  function chooseRadio(name: string): void {
    const radio = Array.from(element().querySelectorAll<HTMLInputElement>('input[type="radio"]')).find(
      (input) => element().querySelector(`label[for="${input.id}"]`)?.textContent?.trim() === name,
    );
    if (!radio) throw new Error(`No radio "${name}"`);
    radio.click();
    fixture.detectChanges();
  }

  async function settle(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
  }

  function expectRequest(method: string, url: string): Promise<TestRequest> {
    return vi.waitFor(() => controller.expectOne((r) => r.method === method && r.url === url));
  }

  function fillNewScrimmage(start: string, end: string): void {
    type('Date', '11/14/2026');
    type('Start time', start);
    type('End time', end);
    type('Title', 'Late skate');
    type('Join instructions', 'Sign up at https://example.com/join');
  }

  it('swaps date fields when the type changes', () => {
    setUp(null);
    expect(hasField('Date')).toBe(true);
    expect(hasField('Start date')).toBe(false);
    const league = Array.from(element().querySelectorAll<HTMLButtonElement>('mat-button-toggle button')).find(
      (b) => b.textContent?.trim() === 'League',
    )!;
    league.click();
    fixture.detectChanges();
    expect(hasField('Date')).toBe(false);
    expect(hasField('Start date')).toBe(true);
    expect(hasField('End date')).toBe(true);
    expect(hasField('Schedule')).toBe(true);
  });

  it('sends naive local times and rolls the end over to the next day', async () => {
    loadExisting();
    type('Start time', '23:00');
    type('End time', '00:30');
    expect(element().querySelector('[data-testid="next-day"]')?.textContent).toContain('Ends the next day');
    click('Save draft');
    const request = await expectRequest('PUT', `${api}/evt0000001`);
    const body = request.request.body as EventRequest;
    expect(body.startsLocal).toBe('2026-11-14T23:00:00');
    expect(body.endsLocal).toBe('2026-11-15T00:30:00');
    expect(body.startDate).toBeNull();
    expect(request.request.headers.get('If-Match')).toBe('"7"');
    request.flush(hostEvent({ version: 8 }));
    await settle();
    expect(success).toHaveBeenCalledWith('Draft saved.');
  });

  it.each([
    ['Free', null, 0],
    ['Fixed fee', '12.50', 1250],
    ['See join instructions', null, null],
  ])('maps the %s fee mode to feeCents', async (mode, amount, feeCents) => {
    loadExisting();
    chooseRadio(mode);
    if (amount) {
      type('Amount', amount);
    }
    click('Save draft');
    const request = await expectRequest('PUT', `${api}/evt0000001`);
    expect((request.request.body as EventRequest).feeCents).toBe(feeCents);
    request.flush(hostEvent());
    await settle();
  });

  it('renders a live preview with outbound links', () => {
    setUp(null);
    type('Join instructions', 'Pay at https://example.com/pay please');
    const preview = element().querySelector('[data-testid="preview"]')!;
    const link = preview.querySelector('a')!;
    expect(link.getAttribute('href')?.startsWith('/out?u=')).toBe(true);
    expect(link.textContent).toBe('https://example.com/pay');
  });

  it('links the skill slider to the skill levels guide in a new tab', () => {
    setUp(null);
    const link = element().querySelector<HTMLAnchorElement>('.skill a[href="/skill-levels"]')!;
    expect(link.getAttribute('target')).toBe('_blank');
    expect(link.getAttribute('rel')).toBe('noopener');
    expect(link.textContent).toContain('What do levels mean? (opens in a new tab)');
  });

  it('requires a venue before creating a new event', () => {
    setUp(null);
    fillNewScrimmage('19:00', '20:00');
    click('Save draft');
    expect(element().querySelector('[data-testid="venue-error"]')?.textContent).toContain('Choose a venue.');
    controller.expectNone(api);
  });

  it('offers a reload when the event is stale and refetches on Reload', async () => {
    loadExisting();
    type('Title', 'Renamed');
    click('Save draft');
    (await expectRequest('PUT', `${api}/evt0000001`)).flush(
      { status: 412, code: 'event_stale', title: 'Stale' },
      { status: 412, statusText: 'Precondition Failed' },
    );
    await settle();
    expect(element().querySelector('[data-testid="stale"]')?.textContent).toContain(
      'This event changed in another tab or device.',
    );
    click('Reload');
    controller.expectOne(`${api}/evt0000001`).flush(hostEvent({ title: 'Changed elsewhere', version: 9 }));
    fixture.detectChanges();
    expect(element().querySelector('[data-testid="stale"]')).toBeNull();
    expect((field('Title') as HTMLInputElement).value).toBe('Changed elsewhere');
  });

  it('lists blocked URLs under join instructions and marks the field invalid', async () => {
    loadExisting();
    click('Save draft');
    (await expectRequest('PUT', `${api}/evt0000001`)).flush(
      {
        status: 422,
        code: 'join_instructions_blocked',
        title: 'Join instructions contain links that are not allowed.',
        blockedUrls: ['https://bad.example/a', 'https://bad.example/b'],
        tooManyUrls: false,
      },
      { status: 422, statusText: 'Unprocessable' },
    );
    await settle();
    const items = Array.from(element().querySelectorAll('[data-testid="blocked-urls"] li')).map((li) => li.textContent);
    expect(items).toEqual(['https://bad.example/a', 'https://bad.example/b']);
    expect(field('Join instructions').getAttribute('aria-invalid')).toBe('true');
  });

  it('maps invalid_event errors onto fields', async () => {
    loadExisting();
    click('Save draft');
    (await expectRequest('PUT', `${api}/evt0000001`)).flush(
      { status: 422, code: 'invalid_event', title: 'Check the highlighted fields.', errors: { title: 'Title is bad.', type: 'Type is bad.' } },
      { status: 422, statusText: 'Unprocessable' },
    );
    await settle();
    expect(element().querySelector('mat-error')?.textContent).toContain('Title is bad.');
    expect(element().querySelector('[data-testid="form-error"]')?.textContent).toContain('Type is bad.');
  });

  it('shows a clock-change error for skipped local times', async () => {
    loadExisting();
    click('Save draft');
    (await expectRequest('PUT', `${api}/evt0000001`)).flush(
      { status: 422, code: 'local_time_skipped', title: 'x', field: 'startsLocal' },
      { status: 422, statusText: 'Unprocessable' },
    );
    await settle();
    expect(field('Start time').getAttribute('aria-invalid')).toBe('true');
    expect(element().textContent).toContain('That time does not exist on that day (clocks change).');
  });

  it('explains a parked publish', async () => {
    loadExisting();
    click('Publish');
    (await expectRequest('PUT', `${api}/evt0000001`)).flush(hostEvent({ version: 8 }));
    (await expectRequest('POST', `${api}/evt0000001/publish`)).flush(
      hostEvent({ version: 9, publishRequestedAt: '2026-10-03T00:00:00Z' }),
      { status: 202, statusText: 'Accepted' },
    );
    await settle();
    expect(element().querySelector('[data-testid="parked"]')?.textContent).toContain(
      'Link checking is delayed. Your event publishes automatically once the links pass.',
    );
  });

  it('explains parked join instructions on a published edit', async () => {
    loadExisting(hostEvent({ status: 'published' }));
    expect(() => button('Publish')).toThrow();
    click('Save changes');
    (await expectRequest('PUT', `${api}/evt0000001`)).flush(hostEvent({ status: 'published', version: 8 }), {
      status: 202,
      statusText: 'Accepted',
    });
    await settle();
    expect(element().querySelector('[data-testid="parked"]')?.textContent).toContain(
      'Your new join instructions go live once the links pass.',
    );
  });

  it('publishes and returns to the dashboard', async () => {
    loadExisting();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl');
    click('Publish');
    (await expectRequest('PUT', `${api}/evt0000001`)).flush(hostEvent({ version: 8 }));
    (await expectRequest('POST', `${api}/evt0000001/publish`)).flush(hostEvent({ status: 'published', version: 9 }));
    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith('/host'));
    expect(success).toHaveBeenCalledWith('Published Friday skate.');
  });

  it.each([
    ['active_limit_reached', 429, 'limit of active events'],
    ['host_inactive', 403, 'host account is not active'],
  ])('explains %s', async (code, status, message) => {
    loadExisting();
    click('Publish');
    (await expectRequest('PUT', `${api}/evt0000001`)).flush(hostEvent({ version: 8 }));
    (await expectRequest('POST', `${api}/evt0000001/publish`)).flush({ status, code, title: 'x' }, { status, statusText: 'Error' });
    await settle();
    expect(element().querySelector('[data-testid="form-error"]')?.textContent).toContain(message);
  });
});
