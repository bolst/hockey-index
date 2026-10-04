import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { environment } from '../../../environments/environment';
import { PublicEvent } from '../../../shared-render/event-summary';
import { EventDetail } from './event-detail';

const HOUR = 3_600_000;

function event(overrides: Partial<PublicEvent> = {}): PublicEvent {
  const start = new Date(Date.now() + 48 * HOUR);
  start.setUTCHours(23, 0, 0, 0);
  return {
    publicId: 'abcdefghij',
    status: 'published',
    type: 'scrimmage',
    title: 'Friday Night Shinny',
    description: '<b>Bring</b> both jerseys',
    venue: {
      publicId: 'venue23456',
      name: 'Mattamy Athletic Centre',
      addressLine: '50 Carlton St',
      city: 'Toronto',
      region: 'ON',
      country: 'CA',
      timeZone: 'America/Toronto',
      latitude: 43.66,
      longitude: -79.38,
    },
    rinkLabel: null,
    startsLocal: '',
    endsLocal: '',
    startsAt: start.toISOString(),
    endsAt: new Date(start.getTime() + 1.5 * HOUR).toISOString(),
    skill: { min: 2, max: 4 },
    feeCents: 1500,
    currency: 'CAD',
    joinInstructions: 'Sign up at https://example.com/join then e-transfer.',
    updatedAt: new Date().toISOString(),
    ...overrides,
  };
}

describe('EventDetail', () => {
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'e/:id/:slug', component: EventDetail },
          { path: 'e/:id', component: EventDetail },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  async function open(url: string, body: PublicEvent | null, status = 200) {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    const request = controller.expectOne(`${environment.apiBaseUrl}/events/abcdefghij`);
    expect(request.request.withCredentials).toBe(false);
    if (body) {
      request.flush(body);
    } else {
      request.flush({ code: 'event_not_found' }, { status, statusText: 'Not Found' });
    }
    await harness.fixture.whenStable();
    return harness.routeNativeElement as HTMLElement;
  }

  it('renders event text as text and join links through /out', async () => {
    const root = await open('/e/abcdefghij/friday-night-shinny', event());
    expect(root.querySelector('h1')?.textContent).toBe('Friday Night Shinny');
    expect(root.querySelector('b')).toBeNull();
    expect(root.textContent).toContain('<b>Bring</b> both jerseys');
    const link = root.querySelector<HTMLAnchorElement>('a[href^="/out"]')!;
    expect(link.getAttribute('href')).toBe(`/out?u=${encodeURIComponent('https://example.com/join')}&e=abcdefghij`);
    expect(link.getAttribute('rel')).toBe('nofollow ugc noopener noreferrer');
    expect(root.textContent).toContain('Skill 2–4');
    const levels = root.querySelector<HTMLAnchorElement>('a[href="/skill-levels"]')!;
    expect(levels.textContent?.trim()).toBe('What do levels mean?');
    expect(root.textContent).toContain('$15 CAD');
    expect(root.textContent).toMatch(/7:00 PM – 8:30 PM E[DS]T/);
  });

  it('says when the event has ended', async () => {
    const past = new Date(Date.now() - 5 * HOUR).toISOString();
    const root = await open('/e/abcdefghij/friday-night-shinny', event({ startsAt: past, endsAt: new Date(Date.now() - 3 * HOUR).toISOString() }));
    expect(root.querySelector('[data-testid="ended-notice"]')?.textContent).toContain('This event has ended.');
  });

  it('marks cancelled events and hides join instructions', async () => {
    const root = await open('/e/abcdefghij/friday-night-shinny', event({ status: 'cancelled', cancelledAt: new Date().toISOString() }));
    expect(root.querySelector('[data-testid="cancelled-notice"]')?.textContent).toContain('cancelled');
    expect(root.textContent).toContain('Cancelled');
    expect(root.querySelector('a[href^="/out"]')).toBeNull();
  });

  it('redirects to the canonical slug', async () => {
    await open('/e/abcdefghij/old-title', event());
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/e/abcdefghij/friday-night-shinny'));
  });

  it('shows not found for a missing event', async () => {
    const root = await open('/e/abcdefghij', null, 404);
    expect(root.querySelector('h1')?.textContent).toContain('Event not found');
  });

  it('offers a retry when the event cannot load', async () => {
    const root = await open('/e/abcdefghij', null, 503);
    expect(root.querySelector('h1')?.textContent).toContain('This event could not load');
    expect(root.querySelector('[data-testid="load-error"]')).not.toBeNull();
  });

  it('opens the report dialog', async () => {
    const root = await open('/e/abcdefghij/friday-night-shinny', event());
    const report = Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.includes('Report'))!;
    report.click();
    await vi.waitFor(() => expect(document.querySelector('[role="dialog"] h2')?.textContent).toContain('Report this event'));
  });
});
