import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { outboundHref, segmentText } from './url-extractor';

/**
 * Renders untrusted text as text. Only http(s) URLs accepted by the server's extractor become links,
 * and they go through the /out interstitial. Never uses innerHTML.
 */
@Component({
  selector: 'app-linkify-text',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'linkify-text' },
  template: `
    @for (segment of segments(); track $index) {
      @if (segment.kind === 'link') {
        <a [href]="segment.href" target="_blank" rel="nofollow ugc noopener noreferrer">{{ segment.text }}</a>
      } @else {
        <ng-container>{{ segment.text }}</ng-container>
      }
    }
  `,
  styles: `
    :host {
      white-space: pre-line;
      overflow-wrap: anywhere;
    }
  `,
})
export class LinkifyText {
  readonly text = input.required<string>();
  readonly eventId = input.required<string>();

  protected readonly segments = computed(() =>
    segmentText(this.text()).map((segment) =>
      segment.kind === 'link'
        ? { kind: segment.kind, text: segment.text, href: outboundHref(segment.url, this.eventId()) }
        : { kind: segment.kind, text: segment.text, href: '' },
    ),
  );
}
