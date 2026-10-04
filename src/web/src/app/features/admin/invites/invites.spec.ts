import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { Invite } from '../../../core/api/admin-api';
import { Notifier } from '../../../shared/notifier/notifier';
import { buttonIn, confirmReason, fieldIn, settle, typeInto } from '../admin-testing';
import { Invites } from './invites';

const api = `${environment.apiBaseUrl}/admin`;
const future = new Date(Date.now() + 86_400_000).toISOString();
const past = new Date(Date.now() - 86_400_000).toISOString();

const invites: Invite[] = [
  { id: 'i1', phone: '+14165550142', createdBy: 'admin-1', expiresAt: future, usedAt: null },
  { id: 'i2', phone: '+14165550143', createdBy: 'admin-1', expiresAt: past, usedAt: null },
  { id: 'i3', phone: '+14165550144', createdBy: null, expiresAt: future, usedAt: past },
];

describe('Invites', () => {
  let fixture: ComponentFixture<Invites>;
  let controller: HttpTestingController;
  let success: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    success = vi.fn();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: Notifier, useValue: { success, error: vi.fn() } }],
    });
    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Invites);
    fixture.detectChanges();
  });

  afterEach(() => controller.verify());

  const element = () => fixture.nativeElement as HTMLElement;
  const row = (id: string) => element().querySelector(`[data-testid="invite-row-${id}"]`)!;

  it('shows formatted phones and invite states', () => {
    controller.expectOne(`${api}/invites`).flush(invites);
    fixture.detectChanges();
    expect(row('i1').textContent).toContain('+1 416-555-0142');
    expect(row('i1').textContent).toContain('Active');
    expect(row('i2').textContent).toContain('Expired');
    expect(row('i3').textContent).toContain('Used');
  });

  it('creates an invite with the default 14 days', () => {
    controller.expectOne(`${api}/invites`).flush([]);
    typeInto(fieldIn(element(), 'Phone'), '+1 416-555-0199');
    typeInto(fieldIn(element(), 'Reason'), 'league organizer');
    buttonIn(element(), 'person_add Create invite').click();
    const request = controller.expectOne({ method: 'POST', url: `${api}/invites` });
    expect(request.request.body).toEqual({ phone: '+14165550199', expiresInDays: 14, reason: 'league organizer' });
    request.flush(invites[0], { status: 201, statusText: 'Created' });
    controller.expectOne({ method: 'GET', url: `${api}/invites` }).flush(invites);
    expect(success).toHaveBeenCalledWith('Invite created.');
  });

  it('rejects a phone without a country code', () => {
    controller.expectOne(`${api}/invites`).flush([]);
    typeInto(fieldIn(element(), 'Phone'), '416-555-0199');
    typeInto(fieldIn(element(), 'Reason'), 'x');
    buttonIn(element(), 'person_add Create invite').click();
    fixture.detectChanges();
    controller.expectNone({ method: 'POST', url: `${api}/invites` });
    expect(element().textContent).toContain('Enter the number with its country code');
  });

  it('revokes an invite with a reason', async () => {
    controller.expectOne(`${api}/invites`).flush(invites);
    fixture.detectChanges();
    buttonIn(element(), 'Revoke invite for +1 416-555-0142').click();
    await confirmReason('sent by mistake', 'Revoke');
    const request = controller.expectOne({ method: 'DELETE', url: `${api}/invites/i1` });
    expect(request.request.body).toEqual({ reason: 'sent by mistake' });
    request.flush(null);
    controller.expectOne(`${api}/invites`).flush([]);
    await settle();
  });
});
