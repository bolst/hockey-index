import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { HostEvent, HostEventStatus, MyEventsResponse } from '../../../core/api/host-events-api';
import { Notifier } from '../../../shared/notifier/notifier';
import { Dashboard } from './dashboard';

const api = `${environment.apiBaseUrl}/host/events`;

function hostEvent(publicId: string, status: HostEventStatus, title: string): HostEvent {
  return {
    publicId,
    status,
    type: 'scrimmage',
    title,
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
    feeCents: 1500,
    currency: 'CAD',
    joinInstructions: 'Email me',
    pendingJoinInstructions: null,
    publishRequestedAt: null,
    hiddenReason: null,
    notice: null,
    publishedAt: null,
    cancelledAt: null,
    archivedAt: null,
    createdAt: '2026-10-01T00:00:00Z',
    updatedAt: '2026-10-01T00:00:00Z',
    version: 1,
    resolvedAmbiguousTimes: [],
  };
}

const limits = { activeListings: 3, maxActiveListings: 10, publishesLast24Hours: 1, maxPublishesPer24Hours: 5 };

describe('Dashboard', () => {
  let fixture: ComponentFixture<Dashboard>;
  let controller: HttpTestingController;
  let success: ReturnType<typeof vi.fn>;

  const element = () => fixture.nativeElement as HTMLElement;

  function setUp(response: MyEventsResponse): void {
    success = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Notifier, useValue: { success, error: vi.fn() } },
      ],
    });
    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Dashboard);
    fixture.detectChanges();
    flushList(response);
  }

  function flushList(response: MyEventsResponse): void {
    controller.expectOne((r) => r.method === 'GET' && r.url === api).flush(response);
    fixture.detectChanges();
  }

  async function chooseMenuItem(eventTitle: string, item: string): Promise<void> {
    element().querySelector<HTMLButtonElement>(`button[aria-label="More actions for ${eventTitle}"]`)!.click();
    fixture.detectChanges();
    const menuItem = await vi.waitFor(() => {
      const match = Array.from(document.querySelectorAll<HTMLElement>('[role="menuitem"]')).find(
        (candidate) => candidate.textContent?.trim().endsWith(item),
      );
      if (!match) throw new Error(`No menu item "${item}"`);
      return match;
    });
    menuItem.click();
    fixture.detectChanges();
  }

  function dialogButton(name: string): Promise<HTMLButtonElement> {
    return vi.waitFor(() => {
      const match = Array.from(document.querySelectorAll<HTMLButtonElement>('mat-dialog-container button')).find(
        (b) => b.textContent?.trim() === name,
      );
      if (!match) throw new Error(`No dialog button "${name}"`);
      return match;
    });
  }

  afterEach(() => {
    controller.verify();
    document.querySelectorAll('.cdk-overlay-container').forEach((overlay) => (overlay.innerHTML = ''));
  });

  it('renders the limits line with exact wording', () => {
    setUp({ events: [], limits });
    expect(element().querySelector('[data-testid="limits"]')?.textContent?.trim()).toBe(
      '3 of 10 active, 1 of 5 publishes today',
    );
    expect(element().querySelector('[data-testid="limit-notice"]')).toBeNull();
  });

  it('explains when a limit is reached', () => {
    setUp({ events: [], limits: { ...limits, activeListings: 10 } });
    expect(element().querySelector('[data-testid="limit-notice"]')?.textContent).toContain('limit of active events');
  });

  it('shows an empty state with a create action', () => {
    setUp({ events: [], limits });
    const link = Array.from(element().querySelectorAll('a')).find((a) => a.textContent?.includes('Create your first event'));
    expect(link?.getAttribute('href')).toBe('/host/events/new');
  });

  it('shows a status chip per event', () => {
    setUp({
      events: [
        hostEvent('aaaaaaaaa1', 'draft', 'Draft game'),
        hostEvent('aaaaaaaaa2', 'published', 'Live game'),
        hostEvent('aaaaaaaaa3', 'cancelled', 'Off game'),
        hostEvent('aaaaaaaaa4', 'archived', 'Old game'),
        hostEvent('aaaaaaaaa5', 'hidden', 'Hidden game'),
      ],
      limits,
    });
    const chips = Array.from(element().querySelectorAll('app-status-chip')).map((chip) => chip.textContent?.trim());
    expect(chips).toEqual(['Draft', 'Published', 'Hidden', 'Cancelled', 'Ended']);
    expect(element().textContent).toContain('Mattamy Athletic Centre, Toronto');
    expect(element().textContent).toContain('Sat, Nov 14, 2026, 9:00 PM EST');
  });

  it('publishes through the menu and reloads', async () => {
    setUp({ events: [hostEvent('aaaaaaaaa1', 'draft', 'Friday skate')], limits });
    await chooseMenuItem('Friday skate', 'Publish');
    const request = controller.expectOne(`${api}/aaaaaaaaa1/publish`);
    expect(request.request.method).toBe('POST');
    request.flush(hostEvent('aaaaaaaaa1', 'published', 'Friday skate'));
    flushList({ events: [hostEvent('aaaaaaaaa1', 'published', 'Friday skate')], limits });
    expect(success).toHaveBeenCalledWith('Published Friday skate.');
  });

  it('explains a parked publish', async () => {
    setUp({ events: [hostEvent('aaaaaaaaa1', 'draft', 'Friday skate')], limits });
    await chooseMenuItem('Friday skate', 'Publish');
    controller
      .expectOne(`${api}/aaaaaaaaa1/publish`)
      .flush(hostEvent('aaaaaaaaa1', 'draft', 'Friday skate'), { status: 202, statusText: 'Accepted' });
    flushList({
      events: [{ ...hostEvent('aaaaaaaaa1', 'draft', 'Friday skate'), publishRequestedAt: '2026-10-03T00:00:00Z' }],
      limits,
    });
    expect(element().querySelector('[data-testid="action-info"]')?.textContent).toContain(
      'Your event publishes automatically once the links pass.',
    );
    expect(element().textContent).toContain('Publishing after link check.');
    expect(success).not.toHaveBeenCalled();
  });

  it('lists blocked URLs when publish is refused', async () => {
    setUp({ events: [hostEvent('aaaaaaaaa1', 'draft', 'Friday skate')], limits });
    await chooseMenuItem('Friday skate', 'Publish');
    controller.expectOne(`${api}/aaaaaaaaa1/publish`).flush(
      {
        status: 422,
        code: 'join_instructions_blocked',
        title: 'Join instructions contain links that are not allowed.',
        blockedUrls: ['https://bad.example/pay'],
        tooManyUrls: false,
      },
      { status: 422, statusText: 'Unprocessable' },
    );
    flushList({ events: [hostEvent('aaaaaaaaa1', 'draft', 'Friday skate')], limits });
    const alert = element().querySelector('[data-testid="action-error"]')!;
    expect(alert.getAttribute('role')).toBe('alert');
    expect(alert.textContent).toContain('https://bad.example/pay');
    expect(alert.querySelector('a')?.getAttribute('href')).toBe('/host/events/aaaaaaaaa1/edit');
  });

  it('explains the daily publish limit', async () => {
    setUp({ events: [hostEvent('aaaaaaaaa1', 'draft', 'Friday skate')], limits });
    await chooseMenuItem('Friday skate', 'Publish');
    controller
      .expectOne(`${api}/aaaaaaaaa1/publish`)
      .flush({ status: 429, code: 'daily_publish_limit_reached', title: 'x' }, { status: 429, statusText: 'Too Many' });
    flushList({ events: [hostEvent('aaaaaaaaa1', 'draft', 'Friday skate')], limits });
    expect(element().querySelector('[data-testid="action-error"]')?.textContent).toContain('daily publish limit');
  });

  it('deletes a draft only after confirmation', async () => {
    setUp({ events: [hostEvent('aaaaaaaaa1', 'draft', 'Friday skate')], limits });
    await chooseMenuItem('Friday skate', 'Delete draft');
    controller.expectNone(`${api}/aaaaaaaaa1`);
    (await dialogButton('Delete draft')).click();
    await vi.waitFor(() => controller.expectOne((r) => r.method === 'DELETE' && r.url === `${api}/aaaaaaaaa1`).flush(null, { status: 204, statusText: 'No Content' }));
    flushList({ events: [], limits });
    expect(success).toHaveBeenCalledWith('Deleted Friday skate.');
  });

  it('does not delete when the confirmation is dismissed', async () => {
    setUp({ events: [hostEvent('aaaaaaaaa1', 'draft', 'Friday skate')], limits });
    await chooseMenuItem('Friday skate', 'Delete draft');
    (await dialogButton('Keep')).click();
    await new Promise((resolve) => setTimeout(resolve));
    controller.expectNone(`${api}/aaaaaaaaa1`);
  });
});
