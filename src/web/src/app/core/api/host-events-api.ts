import { HttpClient, HttpHeaders, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../../environments/environment';
import { EventType } from './discovery-api';

export type HostEventStatus = 'draft' | 'published' | 'cancelled' | 'archived' | 'hidden';
export type Currency = 'CAD' | 'USD';

export interface SkillRange {
  min: number;
  max: number;
}

export interface HostEventVenue {
  publicId: string;
  name: string;
  addressLine: string;
  city: string;
  region: string;
  country: string;
  timeZone: string;
}

/** Body of POST /v1/host/events and PUT /v1/host/events/{id}. */
export interface EventRequest {
  venueId: string;
  rinkLabel: string | null;
  type: EventType;
  title: string;
  description: string | null;
  /** Scrimmage only: venue wall time without offset, `YYYY-MM-DDTHH:mm:00`. */
  startsLocal: string | null;
  endsLocal: string | null;
  /** League/tournament only: `YYYY-MM-DD`; `endDate` is inclusive. */
  startDate: string | null;
  endDate: string | null;
  scheduleText: string | null;
  skill: SkillRange;
  /** Null means "see join instructions". */
  feeCents: number | null;
  currency: Currency | null;
  joinInstructions: string;
}

export interface HostEvent {
  publicId: string;
  status: HostEventStatus;
  type: EventType;
  title: string;
  description: string | null;
  venue: HostEventVenue;
  rinkLabel: string | null;
  startsLocal: string;
  endsLocal: string;
  startsAt: string;
  endsAt: string;
  startDate: string | null;
  endDate: string | null;
  scheduleText: string | null;
  skill: SkillRange;
  feeCents: number | null;
  currency: Currency;
  joinInstructions: string;
  pendingJoinInstructions: string | null;
  publishRequestedAt: string | null;
  hiddenReason: string | null;
  notice: string | null;
  publishedAt: string | null;
  cancelledAt: string | null;
  archivedAt: string | null;
  createdAt: string;
  updatedAt: string;
  /** Same value as the ETag header, unquoted. Send back in If-Match. */
  version: number;
  resolvedAmbiguousTimes: string[];
}

export interface HostEventLimits {
  activeListings: number;
  maxActiveListings: number;
  publishesLast24Hours: number;
  maxPublishesPer24Hours: number;
}

export interface MyEventsResponse {
  events: HostEvent[];
  limits: HostEventLimits;
}

/**
 * Result of a write that can park: 200 applied the change, 202 parked it until the link scanner is
 * back (publish waits in `publishRequestedAt`, an edit's join text in `pendingJoinInstructions`).
 */
export interface EventWriteResult {
  event: HostEvent;
  parked: boolean;
}

function toWriteResult(response: HttpResponse<HostEvent>): EventWriteResult {
  return { event: response.body as HostEvent, parked: response.status === 202 };
}

@Injectable({ providedIn: 'root' })
export class HostEventsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/host/events`;

  list(): Observable<MyEventsResponse> {
    return this.http.get<MyEventsResponse>(this.base);
  }

  get(publicId: string): Observable<HostEvent> {
    return this.http.get<HostEvent>(this.url(publicId));
  }

  create(request: EventRequest): Observable<HostEvent> {
    return this.http.post<HostEvent>(this.base, request);
  }

  /** `version` is the event's last-seen version; a stale one fails with 412 `event_stale`. */
  update(publicId: string, request: EventRequest, version: number): Observable<EventWriteResult> {
    return this.http
      .put<HostEvent>(this.url(publicId), request, {
        headers: new HttpHeaders({ 'If-Match': `"${version}"` }),
        observe: 'response',
      })
      .pipe(map(toWriteResult));
  }

  delete(publicId: string): Observable<void> {
    return this.http.delete<void>(this.url(publicId));
  }

  publish(publicId: string): Observable<EventWriteResult> {
    return this.http
      .post<HostEvent>(`${this.url(publicId)}/publish`, null, { observe: 'response' })
      .pipe(map(toWriteResult));
  }

  cancel(publicId: string): Observable<HostEvent> {
    return this.http.post<HostEvent>(`${this.url(publicId)}/cancel`, null);
  }

  duplicate(publicId: string): Observable<HostEvent> {
    return this.http.post<HostEvent>(`${this.url(publicId)}/duplicate`, null);
  }

  private url(publicId: string): string {
    return `${this.base}/${encodeURIComponent(publicId)}`;
  }
}
