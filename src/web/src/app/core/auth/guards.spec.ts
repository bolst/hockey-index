import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, CanActivateFn, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { Me } from '../api/auth-api';
import { adminGuard, hostGuard, safeReturnUrl } from './guards';
import { Session } from './session';

const host: Me = {
  id: '0192f0a4-0000-7000-8000-000000000001',
  phone: '+14165550100',
  email: null,
  isVoip: false,
  isAdmin: false,
  createdAt: '2026-10-01T00:00:00Z',
};

describe('route guards', () => {
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  function run(guard: CanActivateFn, url: string): Promise<boolean | UrlTree> {
    return TestBed.runInInjectionContext(
      () => guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot) as Promise<boolean | UrlTree>,
    );
  }

  async function respondMe(result: Promise<boolean | UrlTree>, me: Me | null): Promise<boolean | UrlTree> {
    await Promise.resolve();
    const request = controller.expectOne(`${environment.apiBaseUrl}/me`);
    if (me) {
      request.flush(me);
    } else {
      request.flush({ status: 401 }, { status: 401, statusText: 'Unauthorized' });
    }
    return result;
  }

  function serialize(result: boolean | UrlTree): string | boolean {
    return result instanceof UrlTree ? TestBed.inject(Router).serializeUrl(result) : result;
  }

  it('hostGuard redirects anonymous visitors to sign-in with a return URL', async () => {
    const result = await respondMe(run(hostGuard, '/host/account'), null);
    expect(serialize(result)).toBe('/host/sign-in?returnUrl=%2Fhost%2Faccount');
    expect(TestBed.inject(Session).isSignedIn()).toBe(false);
  });

  it('hostGuard admits a signed-in host and caches the session', async () => {
    expect(await respondMe(run(hostGuard, '/host'), host)).toBe(true);
    expect(await run(hostGuard, '/host/account')).toBe(true);
  });

  it('shares one /me request between concurrent guards', async () => {
    const first = run(hostGuard, '/host');
    const second = run(hostGuard, '/host/account');
    await respondMe(first, host);
    expect(await second).toBe(true);
  });

  it('redirects to sign-in with a notice when the session check fails', async () => {
    const result = run(hostGuard, '/host/account');
    await Promise.resolve();
    controller.expectOne(`${environment.apiBaseUrl}/me`).error(new ProgressEvent('error'));
    expect(serialize(await result)).toBe('/host/sign-in?returnUrl=%2Fhost%2Faccount&unavailable=1');
    expect(TestBed.inject(Session).isLoaded()).toBe(false);
  });

  it('adminGuard sends non-admin hosts to the dashboard', async () => {
    const result = await respondMe(run(adminGuard, '/admin'), host);
    expect(serialize(result)).toBe('/host');
  });

  it('adminGuard admits admins', async () => {
    expect(await respondMe(run(adminGuard, '/admin'), { ...host, isAdmin: true })).toBe(true);
  });

  it('adminGuard redirects anonymous visitors to sign-in', async () => {
    const result = await respondMe(run(adminGuard, '/admin/queue'), null);
    expect(serialize(result)).toBe('/host/sign-in?returnUrl=%2Fadmin%2Fqueue');
  });

  it('does not cache a failed session load other than 401', async () => {
    const result = run(hostGuard, '/host');
    await Promise.resolve();
    controller.expectOne(`${environment.apiBaseUrl}/me`).flush(null, { status: 503, statusText: 'Unavailable' });
    expect(serialize(await result)).toBe('/host/sign-in?returnUrl=%2Fhost&unavailable=1');
    expect(TestBed.inject(Session).isLoaded()).toBe(false);
  });
});

describe('safeReturnUrl', () => {
  it.each([
    ['/host/account', '/host/account'],
    ['//evil.example', '/host'],
    ['/\\evil.example', '/host'],
    ['https://evil.example', '/host'],
    [null, '/host'],
    ['', '/host'],
  ])('maps %s to %s', (input, expected) => {
    expect(safeReturnUrl(input)).toBe(expected);
  });
});
