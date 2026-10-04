import { HttpInterceptorFn, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { Session } from '../../../core/auth/session';
import { EmailLink, EmailLinkPurpose } from './email-link';

const me = {
  id: '0192f0a4-0000-7000-8000-000000000001',
  phone: '+14165550100',
  email: 'host@example.com',
  isVoip: false,
  isAdmin: false,
  createdAt: '2026-10-01T00:00:00Z',
};

describe('EmailLink', () => {
  let calls: string[];
  let controller: HttpTestingController;

  function setUp(purpose: EmailLinkPurpose, hash: string) {
    calls = [];
    history.replaceState(null, '', `/auth/email/${purpose}?x=1${hash}`);
    const realReplaceState = history.replaceState.bind(history);
    vi.spyOn(history, 'replaceState').mockImplementation((...args) => {
      calls.push(`replaceState:${location.hash}`);
      realReplaceState(...args);
    });
    const recordRequests: HttpInterceptorFn = (request, next) => {
      calls.push(`http:${location.hash}`);
      return next(request);
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        provideHttpClient(withInterceptors([recordRequests])),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { data: { purpose } } } },
      ],
    });
    controller = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(EmailLink);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => {
    controller.verify();
    vi.restoreAllMocks();
  });

  it('clears the fragment with replaceState on load, before any request', () => {
    setUp('confirm', '#t=secret-token');
    expect(calls).toEqual(['replaceState:#t=secret-token']);
    expect(location.hash).toBe('');
    expect(location.pathname + location.search).toBe('/auth/email/confirm?x=1');
    controller.expectNone(() => true);
  });

  it('POSTs the token to /me/email/confirm only after the click, after the fragment is gone', async () => {
    const fixture = setUp('confirm', '#t=secret-token');
    (fixture.nativeElement as HTMLElement).querySelector('button')!.click();

    const request = controller.expectOne(`${environment.apiBaseUrl}/me/email/confirm`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ token: 'secret-token' });
    expect(calls).toEqual(['replaceState:#t=secret-token', 'http:']);

    request.flush(me);
    await fixture.whenStable();
    expect(TestBed.inject(Session).me()).toEqual(me);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Your email is confirmed.');
  });

  it('POSTs a login link token to /auth/email/complete', async () => {
    const fixture = setUp('login', '#t=login-token');
    (fixture.nativeElement as HTMLElement).querySelector('button')!.click();

    const request = controller.expectOne(`${environment.apiBaseUrl}/auth/email/complete`);
    expect(request.request.body).toEqual({ token: 'login-token' });
    expect(calls[0]).toBe('replaceState:#t=login-token');
    request.flush(me);
    await fixture.whenStable();
    expect(TestBed.inject(Session).isSignedIn()).toBe(true);
  });

  it('shows a fallback without a token and sends nothing', () => {
    const fixture = setUp('confirm', '');
    expect((fixture.nativeElement as HTMLElement).querySelector('button')).toBeNull();
    expect(calls).toEqual([]);
  });
});
