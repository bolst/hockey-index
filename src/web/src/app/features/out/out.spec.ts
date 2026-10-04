import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { environment } from '../../../environments/environment';
import { Out } from './out';

describe('Out', () => {
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: 'out', component: Out }]), provideHttpClient(), provideHttpClientTesting()],
    });
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  async function open(query: string): Promise<{ harness: RouterTestingHarness; root: HTMLElement }> {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(`/out?${query}`);
    return { harness, root: harness.routeNativeElement as HTMLElement };
  }

  function respond(body: object): void {
    controller
      .expectOne(
        (request) =>
          request.url === `${environment.apiBaseUrl}/out/check` &&
          request.params.get('u') === 'https://example.com/join?x=1' &&
          request.params.get('e') === 'abcdefghij' &&
          !request.withCredentials,
      )
      .flush(body);
  }

  it('shows the destination host and continue link after the API confirms it', async () => {
    const { harness, root } = await open(`u=${encodeURIComponent('https://example.com/join?x=1')}&e=abcdefghij`);
    respond({ status: 'ok', url: 'https://example.com/join?x=1', host: 'example.com', finalHost: 'example.com' });
    await harness.fixture.whenStable();

    expect(root.querySelector('h1')?.textContent).toContain('You are leaving Hockey Index');
    expect(root.querySelector('[data-testid="destination-host"]')?.textContent?.trim()).toBe('example.com');
    const links = Array.from(root.querySelectorAll<HTMLAnchorElement>('a'));
    const proceed = links.find((a) => a.textContent?.includes('Continue to example.com'))!;
    expect(proceed.getAttribute('href')).toBe('https://example.com/join?x=1');
    expect(proceed.getAttribute('rel')).toBe('nofollow ugc noopener noreferrer');
    const back = links.find((a) => a.textContent?.includes('Go back'))!;
    expect(back.getAttribute('href')).toBe('/e/abcdefghij');
  });

  it('mentions a redirect to a different host', async () => {
    const { harness, root } = await open(`u=${encodeURIComponent('https://example.com/join?x=1')}&e=abcdefghij`);
    respond({ status: 'ok', url: 'https://example.com/join?x=1', host: 'example.com', finalHost: 'forms.example.net' });
    await harness.fixture.whenStable();
    expect(root.textContent).toContain('redirects to forms.example.net');
  });

  it('refuses links the API does not recognize', async () => {
    const { harness, root } = await open(`u=${encodeURIComponent('https://example.com/join?x=1')}&e=abcdefghij`);
    respond({ status: 'unrecognized' });
    await harness.fixture.whenStable();
    expect(root.querySelector('h1')?.textContent).toContain('We cannot open this link');
    expect(root.querySelector('a[rel~="noopener"]')).toBeNull();
  });

  it('does not call the API for malformed requests', async () => {
    const { harness, root } = await open('u=javascript:alert(1)&e=abcdefghij');
    await harness.fixture.whenStable();
    expect(root.querySelector('h1')?.textContent).toContain('We cannot open this link');
  });
});
