import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Venue } from './venues-api';

export interface ReportSummary {
  reason: string;
  details: string | null;
  createdAt: string;
}

export interface QueueItem {
  publicId: string;
  title: string;
  status: string;
  hiddenReason: string | null;
  hostId: string;
  startsAt: string;
  endsAt: string;
  unreviewedReports: number;
  reports: ReportSummary[];
}

export interface AdminEventSummary {
  publicId: string;
  title: string;
  status: string;
  hiddenReason: string | null;
  startsAt: string;
  endsAt: string;
}

export interface AdminHost {
  id: string;
  phone: string | null;
  email: string | null;
  status: string;
  isAdmin: boolean;
  bannedAt: string | null;
  createdAt: string;
  lastLoginAt: string | null;
  events: AdminEventSummary[];
}

export interface BanResponse {
  hostId: string;
  status: string;
  eventsHidden: number;
}

export type BlocklistKind = 'domain' | 'phone';

export interface BlocklistEntry {
  id: string;
  kind: BlocklistKind;
  value: string;
  reason: string;
  createdBy: string | null;
  createdAt: string;
}

export interface Invite {
  id: string;
  phone: string;
  createdBy: string | null;
  expiresAt: string;
  usedAt: string | null;
}

export type BreakerName = 'otp_sms' | 'mapbox' | 'webrisk';

export interface Breaker {
  name: BreakerName;
  isOpen: boolean;
  openUntil: string | null;
  reason: string | null;
  openedBy: string | null;
}

export type AdminVenue = Omit<Venue, 'distanceMeters'>;

/** Null fields stay unchanged. */
export interface VenueEdit {
  name?: string;
  addressLine?: string;
  city?: string;
  region?: string;
  country?: string;
  latitude?: number;
  longitude?: number;
  timeZone?: string;
}

export interface AuditEntry {
  id: number;
  actorId: string;
  action: string;
  targetType: string;
  targetId: string;
  reason: string;
  /** JSON text. */
  metadata: string | null;
  createdAt: string;
}

export interface AuditPage {
  items: AuditEntry[];
  nextCursor: number | null;
}

/** /v1/admin/*. Every mutation takes a required `reason` and writes one audit row. */
@Injectable({ providedIn: 'root' })
export class AdminApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/admin`;

  queue(): Observable<QueueItem[]> {
    return this.http.get<QueueItem[]>(`${this.base}/queue`);
  }

  hideEvent(publicId: string, reason: string): Observable<{ event: AdminEventSummary }> {
    return this.eventAction(publicId, 'hide', reason);
  }

  restoreEvent(publicId: string, reason: string): Observable<{ event: AdminEventSummary }> {
    return this.eventAction(publicId, 'restore', reason);
  }

  clearHiddenReason(publicId: string, reason: string): Observable<{ event: AdminEventSummary }> {
    return this.eventAction(publicId, 'clear-hidden-reason', reason);
  }

  host(hostId: string): Observable<AdminHost> {
    return this.http.get<AdminHost>(`${this.base}/hosts/${encodeURIComponent(hostId)}`);
  }

  banHost(hostId: string, reason: string): Observable<BanResponse> {
    return this.http.post<BanResponse>(`${this.base}/hosts/${encodeURIComponent(hostId)}/ban`, { reason });
  }

  unbanHost(hostId: string, reason: string): Observable<unknown> {
    return this.http.post(`${this.base}/hosts/${encodeURIComponent(hostId)}/unban`, { reason });
  }

  blocklist(): Observable<BlocklistEntry[]> {
    return this.http.get<BlocklistEntry[]>(`${this.base}/blocklist`);
  }

  addBlocklistEntry(kind: BlocklistKind, value: string, reason: string): Observable<BlocklistEntry> {
    return this.http.post<BlocklistEntry>(`${this.base}/blocklist`, { kind, value, reason });
  }

  removeBlocklistEntry(id: string, reason: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/blocklist/${encodeURIComponent(id)}`, { body: { reason } });
  }

  invites(): Observable<Invite[]> {
    return this.http.get<Invite[]>(`${this.base}/invites`);
  }

  createInvite(phone: string, expiresInDays: number, reason: string): Observable<Invite> {
    return this.http.post<Invite>(`${this.base}/invites`, { phone, expiresInDays, reason });
  }

  revokeInvite(id: string, reason: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/invites/${encodeURIComponent(id)}`, { body: { reason } });
  }

  breakers(): Observable<Breaker[]> {
    return this.http.get<Breaker[]>(`${this.base}/breakers`);
  }

  changeBreaker(name: BreakerName, action: 'open' | 'close', reason: string, minutes?: number): Observable<unknown> {
    return this.http.post(`${this.base}/breakers/${encodeURIComponent(name)}`, {
      action,
      reason,
      ...(action === 'open' && minutes ? { minutes } : {}),
    });
  }

  venues(query: string): Observable<AdminVenue[]> {
    const params = query.trim() ? new HttpParams().set('q', query.trim()) : undefined;
    return this.http.get<AdminVenue[]>(`${this.base}/venues`, { params });
  }

  updateVenue(publicId: string, edit: VenueEdit, reason: string): Observable<AdminVenue> {
    return this.http.put<AdminVenue>(`${this.base}/venues/${encodeURIComponent(publicId)}`, { ...edit, reason });
  }

  mergeVenue(publicId: string, intoId: string, reason: string): Observable<AdminVenue> {
    return this.http.post<AdminVenue>(`${this.base}/venues/${encodeURIComponent(publicId)}/merge`, { intoId, reason });
  }

  audit(cursor: number | null, limit = 50): Observable<AuditPage> {
    let params = new HttpParams().set('limit', limit);
    if (cursor !== null) {
      params = params.set('cursor', cursor);
    }
    return this.http.get<AuditPage>(`${this.base}/audit`, { params });
  }

  private eventAction(publicId: string, action: string, reason: string): Observable<{ event: AdminEventSummary }> {
    return this.http.post<{ event: AdminEventSummary }>(
      `${this.base}/events/${encodeURIComponent(publicId)}/${action}`,
      { reason },
    );
  }
}
