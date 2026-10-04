import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { environment } from '../../../../environments/environment';
import { AdminHost } from '../../../core/api/admin-api';
import { Notifier } from '../../../shared/notifier/notifier';
import { buttonIn, confirmReason, settle } from '../admin-testing';
import { HostDetail } from './host-detail';

const api = `${environment.apiBaseUrl}/admin`;
const hostId = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';

const host: AdminHost = {
  id: hostId,
  phone: '+14165550142',
  email: 'host@example.com',
  status: 'active',
  isAdmin: false,
  bannedAt: null,
  createdAt: '2026-09-01T12:00:00Z',
  lastLoginAt: null,
  events: [
    {
      publicId: 'ev1',
      title: 'Friday shinny',
      status: 'published',
      hiddenReason: null,
      startsAt: '2026-10-09T23:00:00Z',
      endsAt: '2026-10-10T00:30:00Z',
    },
  ],
};

describe('HostDetail', () => {
  let controller: HttpTestingController;
  let harness: RouterTestingHarness;
  let success: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    success = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'admin/hosts/:id', component: HostDetail }]),
        { provide: Notifier, useValue: { success, error: vi.fn() } },
      ],
    });
    controller = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create(`/admin/hosts/${hostId}`);
  });

  afterEach(() => controller.verify());

  const element = () => harness.routeNativeElement as HTMLElement;

  function loadHost(value: AdminHost): void {
    controller.expectOne(`${api}/hosts/${hostId}`).flush(value);
    harness.detectChanges();
  }

  it('shows host facts with a formatted phone and their events', () => {
    loadHost(host);
    expect(element().textContent).toContain('+1 416-555-0142');
    expect(element().textContent).toContain('host@example.com');
    expect(element().textContent).toContain('active');
    expect(element().querySelector('a[href="/e/ev1"]')?.textContent).toContain('Friday shinny');
  });

  it('bans with a reason and reports how many events were hidden', async () => {
    loadHost(host);
    buttonIn(element(), 'block Ban').click();
    await confirmReason('fake listings', 'Ban');
    const request = controller.expectOne(`${api}/hosts/${hostId}/ban`);
    expect(request.request.body).toEqual({ reason: 'fake listings' });
    request.flush({ hostId, status: 'banned', eventsHidden: 3 });
    expect(success).toHaveBeenCalledWith('Banned. 3 events hidden.');
    loadHost({ ...host, status: 'banned', bannedAt: '2026-10-03T12:00:00Z' });
    expect(buttonIn(element(), 'lock_open Unban')).toBeTruthy();
    await settle();
  });

  it('explains a 409 cannot_ban_self', async () => {
    loadHost(host);
    buttonIn(element(), 'block Ban').click();
    await confirmReason('test', 'Ban');
    controller
      .expectOne(`${api}/hosts/${hostId}/ban`)
      .flush({ title: 'Conflict', code: 'cannot_ban_self' }, { status: 409, statusText: 'Conflict' });
    harness.detectChanges();
    expect(element().querySelector('[role="alert"]')?.textContent).toContain('You cannot ban your own account.');
    await settle();
  });

  it('shows a not found message on 404', () => {
    controller
      .expectOne(`${api}/hosts/${hostId}`)
      .flush({ title: 'Not Found' }, { status: 404, statusText: 'Not Found' });
    harness.detectChanges();
    expect(element().querySelector('[role="alert"]')?.textContent).toContain('No host with that ID.');
  });
});
