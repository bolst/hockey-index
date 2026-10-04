import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../environments/environment';
import { App } from './app';
import { routes } from './app.routes';

function heading(root: HTMLElement, level: number): string | undefined {
  return root.querySelector(`h${level}`)?.textContent?.trim();
}

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('renders the discover route at / and falls back to the IP location lookup', async () => {
    const fixture = TestBed.createComponent(App);
    await TestBed.inject(Router).navigateByUrl('/');
    const controller = TestBed.inject(HttpTestingController);
    await vi.waitFor(() => controller.expectOne(`${environment.apiBaseUrl}/geo/ip`).flush({ latitude: null, longitude: null }));
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(heading(compiled.querySelector('main')!, 1)).toBe('Find pickup hockey near you');
    expect(compiled.querySelector('nav[aria-label="Main"]')?.textContent).toContain('For hosts');
    await vi.waitFor(() => expect(compiled.textContent).toContain('Where do you want to play?'));
    controller.verify();
  });

  it('keeps the skip link first and marks the active nav link with aria-current', async () => {
    const fixture = TestBed.createComponent(App);
    await TestBed.inject(Router).navigateByUrl('/');
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    const skipLink = compiled.querySelector('a')!;
    expect(skipLink.textContent).toContain('Skip to content');
    expect(skipLink.getAttribute('href')).toBe('#main');
    const current = compiled.querySelector('nav[aria-label="Main"] [aria-current="page"]');
    expect(current?.textContent).toContain('Find hockey');
  });

  it('offers a labelled menu button for narrow screens', async () => {
    const fixture = TestBed.createComponent(App);
    await TestBed.inject(Router).navigateByUrl('/');
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('button[aria-label="Open navigation menu"]')).toBeTruthy();
    const button = await vi.waitFor(() => {
      const loaded = root.querySelector<HTMLButtonElement>('app-nav-menu button[aria-label="Open navigation menu"]');
      expect(loaded).toBeTruthy();
      return loaded!;
    });
    button.click();
    await vi.waitFor(() => expect(document.querySelectorAll('[role="menuitem"]')).toHaveLength(2));
    const items = Array.from(document.querySelectorAll('[role="menuitem"]')).map((item) => item.textContent?.trim());
    expect(items).toEqual([expect.stringContaining('Find hockey'), expect.stringContaining('For hosts')]);
  });

  it('links the footer to the skill levels guide, which lazy-loads with its title', async () => {
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/host/sign-in');
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const link = root.querySelector<HTMLAnchorElement>('footer nav[aria-label="Footer"] a')!;
    expect(link.textContent?.trim()).toBe('Skill levels');
    expect(link.getAttribute('href')).toBe('/skill-levels');
    await router.navigateByUrl('/skill-levels');
    await fixture.whenStable();
    expect(heading(root.querySelector('main')!, 1)).toBe('Skill levels');
    expect(TestBed.inject(Title).getTitle()).toBe('Skill levels · Hockey Index');
  });

  it('lazy-loads the host sign-in page', async () => {
    const fixture = TestBed.createComponent(App);
    await TestBed.inject(Router).navigateByUrl('/host/sign-in');
    await fixture.whenStable();
    const signIn = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('app-sign-in')!;
    expect(heading(signIn, 1)).toBe('Host sign in');
  });
});
