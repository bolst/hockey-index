import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { credentialsInterceptor } from './credentials.interceptor';

describe('credentialsInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  const api = environment.apiBaseUrl;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([credentialsInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  function expectPlain(url: string): void {
    http.get(url).subscribe();
    const request = controller.expectOne(url);
    expect(request.request.withCredentials).toBe(false);
    expect(request.request.headers.keys()).toEqual([]);
  }

  function expectCredentialed(url: string): void {
    http.get(url).subscribe();
    const request = controller.expectOne(url);
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.headers.get('X-HI-Requested-With')).toBe('hockey-index');
  }

  it('leaves public GET /search without custom headers or credentials', () => {
    expectPlain(`${api}/search?lat=43.6&lng=-79.4`);
  });

  it('leaves public event detail requests untouched', () => {
    expectPlain(`${api}/events/abcdefghij`);
  });

  it.each(['auth/otp/start', 'me', 'me/events', 'host/events', 'admin/queue'])(
    'adds credentials and header for /%s',
    (path) => expectCredentialed(`${api}/${path}`),
  );

  it.each(['venues', 'venues/autocomplete?q=mattamy', 'venues/abcdefghij'])(
    'adds credentials and header for host-only venue path /%s',
    (path) => expectCredentialed(`${api}/${path}`),
  );

  it('does not match a path that only shares a prefix with venues', () => {
    expectPlain(`${api}/venuesearch`);
  });

  it('keeps the query string out of segment matching', () => {
    expectCredentialed(`${api}/host/events?status=draft`);
    expectPlain(`${api}/search?next=/host/events`);
  });

  it('does not match a path that only shares a prefix with a credentialed segment', () => {
    expectPlain(`${api}/hosted-thing`);
    expectPlain(`${api}/menu`);
    expectPlain(`${api}/authority`);
    expectPlain(`${api}/administrators`);
  });

  it('does not treat dot segments as credentialed paths', () => {
    expectPlain(`${api}/search/../../other`);
  });

  it('leaves third-party URLs untouched', () => {
    expectPlain('https://example.com/v1/host/events');
    expectPlain('https://tile.openstreetmap.org/10/286/373.png');
  });

  it('leaves look-alike API hosts untouched', () => {
    const origin = new URL(api).origin;
    expectPlain(`${origin}.evil.example/v1/host/events`);
  });
});
