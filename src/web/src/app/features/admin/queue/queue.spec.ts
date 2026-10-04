import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { QueueItem } from '../../../core/api/admin-api';
import { Notifier } from '../../../shared/notifier/notifier';
import { buttonIn, confirmReason, settle } from '../admin-testing';
import { Queue } from './queue';

const api = `${environment.apiBaseUrl}/admin`;

const item: QueueItem = {
  publicId: 'ev1',
  title: 'Friday night shinny',
  status: 'published',
  hiddenReason: null,
  hostId: 'host-1',
  startsAt: '2026-10-09T23:00:00Z',
  endsAt: '2026-10-10T00:30:00Z',
  unreviewedReports: 2,
  reports: [{ reason: 'spam', details: 'Link to a casino', createdAt: '2026-10-01T12:00:00Z' }],
};

describe('Queue', () => {
  let fixture: ComponentFixture<Queue>;
  let controller: HttpTestingController;
  let success: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    success = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: Notifier, useValue: { success, error: vi.fn() } },
      ],
    });
    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Queue);
    fixture.detectChanges();
  });

  afterEach(() => controller.verify());

  const element = () => fixture.nativeElement as HTMLElement;

  it('renders items with title link, status and host link', () => {
    controller.expectOne(`${api}/queue`).flush([item]);
    fixture.detectChanges();
    const card = element().querySelector('[data-testid="queue-item-ev1"]')!;
    expect(card.querySelector('a[href="/e/ev1"]')?.textContent).toContain('Friday night shinny');
    expect(card.querySelector('a[href="/admin/hosts/host-1"]')).not.toBeNull();
    expect(card.textContent).toContain('published');
    expect(card.textContent).toContain('2 unreviewed reports');
    expect(card.textContent).toContain('Reports (1)');
  });

  it('shows an empty state when nothing needs review', () => {
    controller.expectOne(`${api}/queue`).flush([]);
    fixture.detectChanges();
    expect(element().querySelector('[role="region"]')?.getAttribute('aria-label')).toBe('Nothing to review');
  });

  it('hides an event with the typed reason, then reloads', async () => {
    controller.expectOne(`${api}/queue`).flush([item]);
    fixture.detectChanges();
    buttonIn(element(), 'visibility_off Hide').click();
    await confirmReason('casino spam', 'Hide');
    const request = controller.expectOne(`${api}/events/ev1/hide`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ reason: 'casino spam' });
    request.flush({ event: { ...item, status: 'hidden' } });
    controller.expectOne(`${api}/queue`).flush([]);
    expect(success).toHaveBeenCalledWith('Event hidden.');
    await settle();
  });
});
