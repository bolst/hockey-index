import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { Turnstile, TurnstileLoader, isMissingSiteKey } from './turnstile';

describe('isMissingSiteKey', () => {
  it('treats empty and placeholder keys as missing', () => {
    expect(isMissingSiteKey('')).toBe(true);
    expect(isMissingSiteKey(null)).toBe(true);
    expect(isMissingSiteKey('PRODUCTION_TURNSTILE_SITE_KEY')).toBe(true);
    expect(isMissingSiteKey('STAGING_TURNSTILE_SITE_KEY')).toBe(true);
  });

  it('accepts real-looking keys', () => {
    expect(isMissingSiteKey('0x4AAAAAAABkMYinukE8nzY')).toBe(false);
    expect(isMissingSiteKey('1x00000000000000000000AA')).toBe(false);
  });
});

describe('Turnstile', () => {
  const original = { ...environment };

  afterEach(() => Object.assign(environment, original));

  function render(): { element: HTMLElement; tokens: (string | null)[]; load: ReturnType<typeof vi.fn> } {
    const load = vi.fn(() => new Promise(() => undefined));
    TestBed.configureTestingModule({ providers: [{ provide: TurnstileLoader, useValue: { load } }] });
    const fixture = TestBed.createComponent(Turnstile);
    const tokens: (string | null)[] = [];
    fixture.componentInstance.tokenChange.subscribe((token) => tokens.push(token));
    fixture.detectChanges();
    TestBed.tick();
    return { element: fixture.nativeElement as HTMLElement, tokens, load };
  }

  it('emits the dev-pass token in development without loading the script', () => {
    Object.assign(environment, { turnstileSiteKey: '', turnstileDevPassToken: 'dev-pass' });
    const { element, tokens, load } = render();
    expect(element.textContent).toContain('Human check skipped in development.');
    expect(tokens).toEqual(['dev-pass']);
    expect(load).not.toHaveBeenCalled();
  });

  it('shows a configuration error for a placeholder key outside development', () => {
    Object.assign(environment, { turnstileSiteKey: 'PRODUCTION_TURNSTILE_SITE_KEY', turnstileDevPassToken: null });
    const { element, tokens, load } = render();
    const alert = element.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('human check is not set up');
    expect(tokens).toEqual([]);
    expect(load).not.toHaveBeenCalled();
  });

  it('loads the widget for a real key', () => {
    Object.assign(environment, { turnstileSiteKey: '0x4AAAAAAABkMYinukE8nzY', turnstileDevPassToken: null });
    const { load } = render();
    expect(load).toHaveBeenCalled();
  });
});
