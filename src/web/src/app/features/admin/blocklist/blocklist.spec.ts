import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { BlocklistEntry } from '../../../core/api/admin-api';
import { Notifier } from '../../../shared/notifier/notifier';
import { buttonIn, confirmReason, fieldIn, settle, typeInto } from '../admin-testing';
import { Blocklist } from './blocklist';

const api = `${environment.apiBaseUrl}/admin`;

function entry(id: string, value: string): BlocklistEntry {
  return { id, kind: 'domain', value, reason: 'spam', createdBy: null, createdAt: '2026-10-01T12:00:00Z' };
}

describe('Blocklist', () => {
  let fixture: ComponentFixture<Blocklist>;
  let controller: HttpTestingController;
  let success: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    success = vi.fn();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: Notifier, useValue: { success, error: vi.fn() } }],
    });
    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Blocklist);
    fixture.detectChanges();
  });

  afterEach(() => controller.verify());

  const element = () => fixture.nativeElement as HTMLElement;

  function fillForm(value: string, reason: string): void {
    typeInto(fieldIn(element(), 'Value'), value);
    typeInto(fieldIn(element(), 'Reason'), reason);
    buttonIn(element(), 'add Add').click();
    fixture.detectChanges();
  }

  it('lists entries and pages at 25 rows', () => {
    const entries = Array.from({ length: 30 }, (_, i) => entry(`b${i}`, `spam${i}.example`));
    controller.expectOne(`${api}/blocklist`).flush(entries);
    fixture.detectChanges();
    expect(element().querySelectorAll('[data-testid^="blocklist-row-"]')).toHaveLength(25);
    expect(element().querySelector('[data-testid="blocklist-row-b0"]')?.textContent).toContain('spam0.example');
  });

  it('adds an entry and reloads', () => {
    controller.expectOne(`${api}/blocklist`).flush([]);
    fillForm('bad.example', 'phishing');
    const request = controller.expectOne({ method: 'POST', url: `${api}/blocklist` });
    expect(request.request.body).toEqual({ kind: 'domain', value: 'bad.example', reason: 'phishing' });
    request.flush(entry('b1', 'bad.example'), { status: 201, statusText: 'Created' });
    controller.expectOne({ method: 'GET', url: `${api}/blocklist` }).flush([entry('b1', 'bad.example')]);
    expect(success).toHaveBeenCalledWith('Blocked bad.example.');
  });

  it('explains a duplicate entry', () => {
    controller.expectOne(`${api}/blocklist`).flush([]);
    fillForm('bad.example', 'phishing');
    controller
      .expectOne({ method: 'POST', url: `${api}/blocklist` })
      .flush({ title: 'Conflict', code: 'blocklist_duplicate' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(element().querySelector('[role="alert"]')?.textContent).toContain('That value is already on the blocklist.');
  });

  it('removes an entry with a reason', async () => {
    controller.expectOne(`${api}/blocklist`).flush([entry('b1', 'bad.example')]);
    fixture.detectChanges();
    buttonIn(element(), 'Remove bad.example').click();
    await confirmReason('false positive', 'Remove');
    const request = controller.expectOne({ method: 'DELETE', url: `${api}/blocklist/b1` });
    expect(request.request.body).toEqual({ reason: 'false positive' });
    request.flush(null);
    controller.expectOne(`${api}/blocklist`).flush([]);
    await settle();
  });
});
