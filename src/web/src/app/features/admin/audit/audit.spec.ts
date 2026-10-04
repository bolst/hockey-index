import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { AuditEntry } from '../../../core/api/admin-api';
import { buttonIn, dialog, settle } from '../admin-testing';
import { Audit } from './audit';

const api = `${environment.apiBaseUrl}/admin`;

function entry(id: number, metadata: string | null = null): AuditEntry {
  return {
    id,
    actorId: 'admin-1',
    action: 'host.ban',
    targetType: 'host',
    targetId: `h${id}`,
    reason: `reason ${id}`,
    metadata,
    createdAt: '2026-10-01T12:00:00Z',
  };
}

describe('Audit', () => {
  let fixture: ComponentFixture<Audit>;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Audit);
    fixture.detectChanges();
  });

  afterEach(() => controller.verify());

  const element = () => fixture.nativeElement as HTMLElement;
  const rows = () => element().querySelectorAll('[data-testid^="audit-row-"]');

  it('loads the next page through nextCursor and appends it', () => {
    const first = controller.expectOne((r) => r.url === `${api}/audit`);
    expect(first.request.params.has('cursor')).toBe(false);
    first.flush({ items: [entry(10), entry(9)], nextCursor: 9 });
    fixture.detectChanges();
    expect(rows()).toHaveLength(2);

    buttonIn(element(), 'Load more').click();
    const second = controller.expectOne((r) => r.url === `${api}/audit`);
    expect(second.request.params.get('cursor')).toBe('9');
    second.flush({ items: [entry(8)], nextCursor: null });
    fixture.detectChanges();
    expect(rows()).toHaveLength(3);
    expect(() => buttonIn(element(), 'Load more')).toThrow();
  });

  it('shows pretty-printed metadata in a dialog', async () => {
    controller.expectOne((r) => r.url === `${api}/audit`).flush({ items: [entry(1, '{"eventsHidden":3}')], nextCursor: null });
    fixture.detectChanges();
    buttonIn(element(), 'Show details').click();
    await settle();
    await vi.waitFor(() => dialog());
    expect(dialog().querySelector('[data-testid="audit-metadata"]')?.textContent).toBe('{\n  "eventsHidden": 3\n}');
  });
});
