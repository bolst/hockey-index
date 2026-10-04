import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { Breaker } from '../../../core/api/admin-api';
import { Notifier } from '../../../shared/notifier/notifier';
import { buttonIn, confirmReason, dialog, fieldIn, settle, submitDialog, typeInto } from '../admin-testing';
import { Breakers } from './breakers';

const api = `${environment.apiBaseUrl}/admin`;

const breakers: Breaker[] = [
  { name: 'otp_sms', isOpen: false, openUntil: null, reason: null, openedBy: null },
  { name: 'mapbox', isOpen: true, openUntil: '2026-10-03T13:00:00Z', reason: 'quota spike', openedBy: 'admin-1' },
];

describe('Breakers', () => {
  let fixture: ComponentFixture<Breakers>;
  let controller: HttpTestingController;
  let success: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    success = vi.fn();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: Notifier, useValue: { success, error: vi.fn() } }],
    });
    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Breakers);
    fixture.detectChanges();
    controller.expectOne(`${api}/breakers`).flush(breakers);
    fixture.detectChanges();
  });

  afterEach(() => controller.verify());

  const card = (name: string) => (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(`[data-testid="breaker-${name}"]`)!;

  it('shows friendly names and open or closed state', () => {
    expect(card('otp_sms').textContent).toContain('SMS codes');
    expect(card('otp_sms').textContent).toContain('Closed — normal');
    expect(card('mapbox').textContent).toContain('Address lookup');
    expect(card('mapbox').textContent).toContain('Open — tripped');
    expect(card('mapbox').textContent).toContain('quota spike');
  });

  it('opens a breaker for the chosen minutes', async () => {
    buttonIn(card('otp_sms'), 'Open').click();
    const trigger = await vi.waitFor(() => {
      const combobox = dialog().querySelector<HTMLElement>('[role="combobox"]');
      if (!combobox) throw new Error('No duration select');
      return combobox;
    });
    await settle();
    trigger.click();
    await settle();
    const option = await vi.waitFor(() => {
      const match = Array.from(document.querySelectorAll<HTMLElement>('[role="option"]')).find(
        (o) => o.textContent?.trim() === '4 hours',
      );
      if (!match) throw new Error('No 4 hours option');
      return match;
    });
    option.click();
    await settle();
    typeInto(fieldIn(dialog(), 'Reason'), 'SMS pumping');
    await submitDialog('Open breaker');
    const request = controller.expectOne({ method: 'POST', url: `${api}/breakers/otp_sms` });
    expect(request.request.body).toEqual({ action: 'open', reason: 'SMS pumping', minutes: 240 });
    request.flush({});
    controller.expectOne(`${api}/breakers`).flush(breakers);
    expect(success).toHaveBeenCalledWith('SMS codes breaker opened.');
  });

  it('closes a breaker with a reason', async () => {
    buttonIn(card('mapbox'), 'Close').click();
    await confirmReason('quota reset', 'Close breaker');
    const request = controller.expectOne({ method: 'POST', url: `${api}/breakers/mapbox` });
    expect(request.request.body).toEqual({ action: 'close', reason: 'quota reset' });
    request.flush({});
    controller.expectOne(`${api}/breakers`).flush(breakers);
    await settle();
  });
});
