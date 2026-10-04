import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { HttpParams } from '@angular/common/http';
import { MatButton } from '@angular/material/button';
import { MatCard, MatCardActions, MatCardContent } from '@angular/material/card';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { environment } from '../../../environments/environment';
import { OutboundLinkCheck } from '../../core/api/discovery-api';
import { PUBLIC_ID_PATTERN } from '../../../shared-render/event-summary';
import { EmptyState } from '../../shared/empty-state/empty-state';

const MAX_URL_LENGTH = 2048;

/**
 * Interstitial for links in join instructions. The API confirms the URL is still in the event's
 * instructions and passed the link scanner before the page offers to continue.
 */
@Component({
  selector: 'app-out',
  imports: [MatButton, MatCard, MatCardContent, MatCardActions, MatIcon, MatProgressBar, RouterLink, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'app-page' },
  templateUrl: './out.html',
  styleUrl: './out.scss',
})
export class Out {
  private readonly params = toSignal(inject(ActivatedRoute).queryParamMap);
  protected readonly url = computed(() => this.params()?.get('u') ?? '');
  protected readonly eventId = computed(() => this.params()?.get('e') ?? '');
  private readonly validRequest = computed(
    () => PUBLIC_ID_PATTERN.test(this.eventId()) && /^https?:\/\//i.test(this.url()) && this.url().length <= MAX_URL_LENGTH,
  );

  protected readonly check = httpResource<OutboundLinkCheck>(() =>
    this.validRequest()
      ? {
          url: `${environment.apiBaseUrl}/out/check`,
          params: new HttpParams().set('u', this.url()).set('e', this.eventId()),
        }
      : undefined,
  );

  protected readonly confirmed = computed(() => {
    const result = this.check.hasValue() ? this.check.value() : null;
    return result?.status === 'ok' ? result : null;
  });
  protected readonly unrecognized = computed(
    () => !this.validRequest() || !!this.check.error() || (this.check.hasValue() && this.check.value().status !== 'ok'),
  );
  protected readonly redirectHost = computed(() => {
    const result = this.confirmed();
    return result?.finalHost && result.finalHost !== result.host ? result.finalHost : null;
  });
  protected readonly backLink = computed(() => (PUBLIC_ID_PATTERN.test(this.eventId()) ? `/e/${this.eventId()}` : '/'));
}
