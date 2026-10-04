import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { AdminVenue } from '../../../core/api/admin-api';
import { Notifier } from '../../../shared/notifier/notifier';
import { buttonIn, dialog, fieldIn, settle, submitDialog, typeInto } from '../admin-testing';
import { Venues } from './venues';

const api = `${environment.apiBaseUrl}/admin`;

const venue: AdminVenue = {
  publicId: 'v1',
  name: 'Mattamy Athletic Centre',
  addressLine: '50 Carlton St',
  city: 'Toronto',
  region: 'ON',
  country: 'CA',
  latitude: 43.6619,
  longitude: -79.3802,
  timeZone: 'America/Toronto',
};

describe('Venues', () => {
  let fixture: ComponentFixture<Venues>;
  let controller: HttpTestingController;
  let success: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    success = vi.fn();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: Notifier, useValue: { success, error: vi.fn() } }],
    });
    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Venues);
    fixture.detectChanges();
    controller.expectOne((r) => r.url === `${api}/venues` && !r.params.has('q')).flush([]);
    fixture.detectChanges();
  });

  afterEach(() => controller.verify());

  const element = () => fixture.nativeElement as HTMLElement;

  function searchFor(q: string, results: AdminVenue[]): void {
    typeInto(fieldIn(element(), 'Search venues'), q);
    buttonIn(element(), 'search Search').click();
    controller.expectOne((r) => r.url === `${api}/venues` && r.params.get('q') === q).flush(results);
    fixture.detectChanges();
  }

  it('searches and lists venues', () => {
    searchFor('mattamy', [venue]);
    expect(element().querySelector('[data-testid="venue-row-v1"]')?.textContent).toContain('Mattamy Athletic Centre');
  });

  it('edits a venue with a reason', async () => {
    searchFor('mattamy', [venue]);
    buttonIn(element(), 'Edit Mattamy Athletic Centre').click();
    await vi.waitFor(() => dialog());
    await settle();
    typeInto(fieldIn(dialog(), 'Name'), 'Mattamy Athletic Centre (Maple Leaf Gardens)');
    typeInto(fieldIn(dialog(), 'Reason'), 'official name');
    await submitDialog('Save');
    const request = controller.expectOne({ method: 'PUT', url: `${api}/venues/v1` });
    expect(request.request.body).toEqual({
      name: 'Mattamy Athletic Centre (Maple Leaf Gardens)',
      addressLine: '50 Carlton St',
      city: 'Toronto',
      region: 'ON',
      country: 'CA',
      latitude: 43.6619,
      longitude: -79.3802,
      timeZone: 'America/Toronto',
      reason: 'official name',
    });
    request.flush({ ...venue, name: 'Mattamy Athletic Centre (Maple Leaf Gardens)' });
    fixture.detectChanges();
    expect(success).toHaveBeenCalledWith('Saved Mattamy Athletic Centre (Maple Leaf Gardens).');
    expect(element().querySelector('[data-testid="venue-row-v1"]')?.textContent).toContain('Maple Leaf Gardens');
  });

  it('explains an invalid merge', async () => {
    searchFor('mattamy', [venue]);
    buttonIn(element(), 'Merge Mattamy Athletic Centre').click();
    await vi.waitFor(() => dialog());
    await settle();
    typeInto(fieldIn(dialog(), 'Merge into venue ID'), 'v1');
    typeInto(fieldIn(dialog(), 'Reason'), 'duplicate');
    await submitDialog('Merge');
    const request = controller.expectOne({ method: 'POST', url: `${api}/venues/v1/merge` });
    expect(request.request.body).toEqual({ intoId: 'v1', reason: 'duplicate' });
    request.flush({ title: 'Conflict', code: 'venue_merge_invalid' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(element().querySelector('[role="alert"]')?.textContent).toContain('These venues cannot be merged.');
  });
});
