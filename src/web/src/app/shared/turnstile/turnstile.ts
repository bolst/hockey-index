import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injectable,
  OnDestroy,
  afterNextRender,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { environment } from '../../../environments/environment';
import { InlineMessage } from '../inline-message/inline-message';

interface TurnstileRenderOptions {
  sitekey: string;
  action?: string;
  callback: (token: string) => void;
  'expired-callback': () => void;
  'error-callback': () => void;
}

interface TurnstileApi {
  render(container: HTMLElement, options: TurnstileRenderOptions): string;
  reset(widgetId: string): void;
  remove(widgetId: string): void;
}

/** Empty, or a deploy-time placeholder such as `PRODUCTION_TURNSTILE_SITE_KEY`. */
export function isMissingSiteKey(siteKey: string | null | undefined): boolean {
  return !siteKey || /^[A-Z0-9_]*TURNSTILE[A-Z0-9_]*$/.test(siteKey) || siteKey.includes('PLACEHOLDER');
}

const SCRIPT_URL = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';

@Injectable({ providedIn: 'root' })
export class TurnstileLoader {
  private readonly document = inject(DOCUMENT);
  private loading: Promise<TurnstileApi> | null = null;

  load(): Promise<TurnstileApi> {
    this.loading ??= new Promise<TurnstileApi>((resolve, reject) => {
      const script = this.document.createElement('script');
      script.src = SCRIPT_URL;
      script.async = true;
      script.onload = () => {
        const api = (this.document.defaultView as (Window & { turnstile?: TurnstileApi }) | null)?.turnstile;
        if (api) {
          resolve(api);
        } else {
          reject(new Error('Turnstile did not load.'));
        }
      };
      script.onerror = () => {
        this.loading = null;
        reject(new Error('Turnstile did not load.'));
      };
      this.document.head.appendChild(script);
    });
    return this.loading;
  }
}

/**
 * Cloudflare Turnstile widget. Without a site key, a development build emits the configured
 * dev-pass token instead (the API's fake verifier accepts any token except empty or "fail*").
 * Any other build with a missing or placeholder key MUST NOT load the script; it shows a
 * configuration error and never emits a token, so the form cannot submit.
 */
@Component({
  selector: 'app-turnstile',
  imports: [InlineMessage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (devPassToken) {
      <p class="dev-note">Human check skipped in development.</p>
    } @else if (misconfigured) {
      <app-inline-message kind="error" data-testid="turnstile-misconfigured">
        The human check is not set up on this site yet, so this form cannot be sent. Please try again later.
      </app-inline-message>
    } @else {
      <div #container></div>
      @if (loadFailed()) {
        <app-inline-message kind="error">The human check could not load. Reload the page and try again.</app-inline-message>
      }
    }
  `,
  styles: `
    :host {
      display: block;
    }

    .dev-note {
      margin: 0;
      font: var(--mat-sys-body-small);
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class Turnstile implements OnDestroy {
  readonly action = input<string>();
  readonly tokenChange = output<string | null>();

  private readonly siteKeyMissing = isMissingSiteKey(environment.turnstileSiteKey);
  protected readonly devPassToken = this.siteKeyMissing ? environment.turnstileDevPassToken : null;
  protected readonly misconfigured = this.siteKeyMissing && !this.devPassToken;
  protected readonly loadFailed = signal(false);
  private readonly container = viewChild<ElementRef<HTMLElement>>('container');
  private readonly loader = inject(TurnstileLoader);
  private api: TurnstileApi | null = null;
  private widgetId: string | null = null;

  constructor() {
    afterNextRender(() => {
      if (this.devPassToken) {
        this.tokenChange.emit(this.devPassToken);
      } else if (!this.misconfigured) {
        void this.renderWidget();
      }
    });
  }

  /** Turnstile tokens are single use; call after every submit that consumed the token. */
  reset(): void {
    if (this.devPassToken) {
      this.tokenChange.emit(this.devPassToken);
      return;
    }
    this.tokenChange.emit(null);
    if (this.api && this.widgetId) {
      this.api.reset(this.widgetId);
    }
  }

  ngOnDestroy(): void {
    if (this.api && this.widgetId) {
      this.api.remove(this.widgetId);
    }
  }

  private async renderWidget(): Promise<void> {
    const container = this.container()?.nativeElement;
    if (!container) {
      return;
    }
    try {
      this.api = await this.loader.load();
    } catch {
      this.loadFailed.set(true);
      return;
    }
    this.widgetId = this.api.render(container, {
      sitekey: environment.turnstileSiteKey,
      action: this.action(),
      callback: (token) => this.tokenChange.emit(token),
      'expired-callback': () => this.tokenChange.emit(null),
      'error-callback': () => this.tokenChange.emit(null),
    });
  }
}
