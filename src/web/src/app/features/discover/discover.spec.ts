import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { DeferBlockBehavior, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { environment } from '../../../environments/environment';
import { SearchResult } from '../../core/api/discovery-api';
import { LocationService, SearchLocation } from '../../core/geo/location';
import { Discover } from './discover';

function result(overrides: Partial<SearchResult> = {}): SearchResult {
  return {
    publicId: 'abcdefghij',
    status: 'published',
    type: 'scrimmage',
    title: 'Friday Night Shinny',
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
    startsLocal: '2030-01-04T19:00:00',
    endsLocal: '2030-01-04T20:30:00',
    startsAt: '2030-01-05T00:00:00Z',
    endsAt: '2030-01-05T01:30:00Z',
    startDate: null,
    endDate: null,
    skill: { min: 2, max: 4 },
    feeCents: 1500,
    currency: 'CAD',
    distanceMiles: 0.4,
    ...overrides,
  };
}

describe('Discover', () => {
  let controller: HttpTestingController;
  let located: SearchLocation | null;

  beforeEach(() => {
    located = null;
    vi.stubGlobal(
      'IntersectionObserver',
      class {
        observe = vi.fn();
        unobserve = vi.fn();
        disconnect = vi.fn();
      },
    );
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: '', component: Discover }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: LocationService, useValue: { locate: () => Promise.resolve(located) } },
      ],
      deferBlockBehavior: DeferBlockBehavior.Manual,
    });
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    controller.verify();
    vi.unstubAllGlobals();
  });

  async function open(url: string): Promise<{ harness: RouterTestingHarness; root: HTMLElement }> {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    TestBed.tick();
    return { harness, root: harness.routeNativeElement as HTMLElement };
  }

  function expectSearch(): TestRequest {
    return controller.expectOne((request) => request.url === `${environment.apiBaseUrl}/search`);
  }

  it('asks for a place when the device and network lookups both fail', async () => {
    const { root } = await open('/');
    await vi.waitFor(() => expect(root.textContent).toContain('Where do you want to play?'));
    expect(root.querySelector('h1')?.textContent).toContain('Find pickup hockey near you');
  });

  it('writes the detected location to the URL with default filters omitted', async () => {
    located = { lat: 43.66123, lng: -79.38456, source: 'device' };
    const { harness } = await open('/');
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/?lat=43.66&lng=-79.38'));
    harness.fixture.detectChanges();
    const request = expectSearch();
    const params = request.request.params;
    expect(params.keys()).toEqual(['lat', 'lng', 'r', 'from', 'days']);
    expect(params.get('r')).toBe('25');
    expect(params.get('days')).toBe('14');
    expect(params.get('from')).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:00:00Z$/);
    expect(request.request.withCredentials).toBe(false);
    request.flush({ events: [], truncated: false });
  });

  it('reads filters from the URL, lists results and marks cancelled events', async () => {
    const { harness, root } = await open('/?lat=43.66&lng=-79.38&r=10&days=7&type=league&smin=2&smax=5&maxFee=2000');
    const request = expectSearch();
    const params = request.request.params;
    expect(params.keys()).toEqual(['lat', 'lng', 'r', 'from', 'days', 'smin', 'smax', 'type', 'maxFee']);
    expect(params.get('type')).toBe('league');
    request.flush({
      events: [result(), result({ publicId: 'bcdefghijk', title: 'Cancelled skate', status: 'cancelled' })],
      truncated: false,
    });
    await harness.fixture.whenStable();

    expect(root.querySelector('#results-heading')?.textContent?.trim()).toBe('2 games within 10 mi');
    const cards = root.querySelectorAll('app-event-card');
    expect(cards).toHaveLength(2);
    expect(cards[0].querySelector('a')?.getAttribute('href')).toBe('/e/abcdefghij/friday-night-shinny');
    expect(cards[0].textContent).not.toContain('Cancelled');
    expect(cards[1].textContent).toContain('Cancelled');
    expect(root.querySelector('[data-testid="truncated-notice"]')).toBeNull();
  });

  it('links the skill filter to the skill levels guide', async () => {
    const { harness, root } = await open('/?lat=43.66&lng=-79.38');
    expectSearch().flush({ events: [], truncated: false });
    await harness.fixture.whenStable();
    const link = root.querySelector<HTMLAnchorElement>('app-discover-filters .skill a')!;
    expect(link.textContent?.trim()).toBe('What do levels mean?');
    expect(link.getAttribute('href')).toBe('/skill-levels');
    expect(link.hasAttribute('target')).toBe(false);
  });

  it('says when the API truncated the results', async () => {
    const { harness, root } = await open('/?lat=43.66&lng=-79.38');
    expectSearch().flush({ events: [result()], truncated: true });
    await harness.fixture.whenStable();
    expect(root.querySelector('[data-testid="truncated-notice"]')?.textContent).toContain('more games than we can show');
    expect(root.querySelector('#results-heading')?.textContent?.trim()).toBe('1+ game within 25 mi');
  });

  it('offers a wider search when nothing matches', async () => {
    const { harness, root } = await open('/?lat=43.66&lng=-79.38');
    expectSearch().flush({ events: [], truncated: false });
    await harness.fixture.whenStable();
    const widen = Array.from(root.querySelectorAll('button')).find((b) => b.textContent?.includes('Search within 50 mi'))!;
    widen.click();
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/?lat=43.66&lng=-79.38&r=50'));
    harness.fixture.detectChanges();
    expect(expectSearch().request.params.get('r')).toBe('50');
  });
});
