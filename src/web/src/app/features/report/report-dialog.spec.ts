import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { environment } from '../../../environments/environment';
import { openReportDialog } from './report-dialog';

describe('ReportDialog', () => {
  let controller: HttpTestingController;
  const tick = async () => {
    TestBed.tick();
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
  };

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  function button(name: string): HTMLButtonElement {
    return Array.from(document.querySelectorAll<HTMLButtonElement>('mat-dialog-container button')).find((b) =>
      b.textContent?.includes(name),
    )!;
  }

  async function chooseReason(label: string): Promise<void> {
    document.querySelector<HTMLElement>('mat-select')!.click();
    await tick();
    Array.from(document.querySelectorAll<HTMLElement>('mat-option'))
      .find((o) => o.textContent?.includes(label))!
      .click();
    await tick();
  }

  async function open() {
    const ref = openReportDialog(TestBed.inject(MatDialog), { publicId: 'abcdefghij', title: 'Friday Night Shinny' });
    await tick();
    return ref;
  }

  it('requires a reason, then posts the report with the dev-pass token', async () => {
    const ref = await open();
    const closed = vi.fn();
    ref.afterClosed().subscribe(closed);
    expect(document.querySelector('mat-select')?.getAttribute('aria-labelledby')).toBeTruthy();

    button('Send report').click();
    await tick();
    expect(document.querySelector('mat-error')?.textContent).toContain('Choose a reason.');

    await chooseReason('Scam');
    const details = document.querySelector<HTMLTextAreaElement>('textarea')!;
    details.value = ' Asked for crypto ';
    details.dispatchEvent(new Event('input'));
    button('Send report').click();

    const request = controller.expectOne(`${environment.apiBaseUrl}/events/abcdefghij/reports`);
    expect(request.request.body).toEqual({ reason: 'scam', details: 'Asked for crypto', turnstileToken: 'dev-pass' });
    expect(request.request.withCredentials).toBe(false);
    request.flush(null, { status: 204, statusText: 'No Content' });
    await vi.waitFor(() => expect(closed).toHaveBeenCalledWith(true));
    await vi.waitFor(() => expect(document.querySelector('mat-snack-bar-container')?.textContent).toContain('A moderator will review'));
  });

  it('explains a rate limit', async () => {
    await open();
    await chooseReason('Spam');
    button('Send report').click();
    controller
      .expectOne(`${environment.apiBaseUrl}/events/abcdefghij/reports`)
      .flush({ code: 'report_limit_reached' }, { status: 429, statusText: 'Too Many Requests' });
    await tick();
    expect(document.querySelector('mat-dialog-container [role="alert"]')?.textContent).toContain('several reports');
  });
});
