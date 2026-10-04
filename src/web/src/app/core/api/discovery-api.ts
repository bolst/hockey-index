import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PublicEvent, PublicEventVenue } from '../../../shared-render/event-summary';

export type EventType = 'scrimmage' | 'league' | 'tournament';
export type PublicEventStatus = 'published' | 'cancelled' | 'archived';

export interface SearchResult {
  publicId: string;
  status: PublicEventStatus;
  type: EventType;
  title: string;
  venue: PublicEventVenue;
  rinkLabel: string | null;
  startsLocal: string;
  endsLocal: string;
  startsAt: string;
  endsAt: string;
  startDate: string | null;
  endDate: string | null;
  skill: { min: number; max: number };
  feeCents: number | null;
  currency: string;
  distanceMiles: number;
}

export interface SearchResponse {
  events: SearchResult[];
  truncated: boolean;
}

export interface IpLocation {
  latitude: number | null;
  longitude: number | null;
}

export type OutboundLinkCheck =
  | { status: 'ok'; url: string; host: string; finalHost?: string | null }
  | { status: 'unrecognized' };

export type ReportReason = 'scam' | 'spam' | 'offensive' | 'inaccurate' | 'other';

export interface ReportRequest {
  reason: ReportReason;
  details?: string;
  turnstileToken: string;
}

/**
 * Public, anonymous endpoints. Requests carry no credentials and no custom headers, so they stay
 * CORS-simple (no preflight) and edge-cacheable. The credentials interceptor leaves these paths alone.
 */
@Injectable({ providedIn: 'root' })
export class DiscoveryApi {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  /** `query` MUST come from `buildSearchQuery` so the URL is canonical and cacheable. */
  search(query: HttpParams): Observable<SearchResponse> {
    return this.http.get<SearchResponse>(`${this.base}/search`, { params: query });
  }

  getEvent(publicId: string): Observable<PublicEvent> {
    return this.http.get<PublicEvent>(`${this.base}/events/${encodeURIComponent(publicId)}`);
  }

  ipLocation(): Observable<IpLocation> {
    return this.http.get<IpLocation>(`${this.base}/geo/ip`);
  }

  checkOutboundLink(url: string, eventId: string): Observable<OutboundLinkCheck> {
    return this.http.get<OutboundLinkCheck>(`${this.base}/out/check`, {
      params: new HttpParams().set('u', url).set('e', eventId),
    });
  }

  report(publicId: string, request: ReportRequest): Observable<void> {
    return this.http.post<void>(`${this.base}/events/${encodeURIComponent(publicId)}/reports`, request);
  }
}
