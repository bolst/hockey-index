import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface Venue {
  publicId: string;
  name: string;
  addressLine: string;
  city: string;
  region: string;
  country: string;
  latitude: number;
  longitude: number;
  timeZone: string;
  distanceMeters: number | null;
}

/** A temporary provider result. Display only: MUST NOT be persisted; POST /v1/venues turns it into a venue. */
export interface AddressSuggestion {
  providerPlaceId: string;
  label: string;
  addressLine: string;
  city: string;
  region: string;
  country: string;
  latitude: number;
  longitude: number;
}

export type SuggestionsStatus = 'ok' | 'not_needed' | 'unavailable' | 'limit_reached';

export interface AutocompleteVenuesResponse {
  venues: Venue[];
  suggestions: AddressSuggestion[];
  suggestionsStatus: SuggestionsStatus;
}

export interface CreateVenueRequest {
  name: string;
  providerPlaceId: string;
  address: string;
  latitude: number;
  longitude: number;
  confirmNew?: boolean;
}

export interface Coordinates {
  lat: number;
  lng: number;
}

@Injectable({ providedIn: 'root' })
export class VenuesApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/venues`;

  autocomplete(query: string, near?: Coordinates | null): Observable<AutocompleteVenuesResponse> {
    let params = new HttpParams().set('q', query);
    if (near) {
      params = params.set('lat', near.lat).set('lng', near.lng);
    }
    return this.http.get<AutocompleteVenuesResponse>(`${this.base}/autocomplete`, { params });
  }

  create(request: CreateVenueRequest): Observable<Venue> {
    return this.http.post<Venue>(this.base, request);
  }

  get(publicId: string): Observable<Venue> {
    return this.http.get<Venue>(`${this.base}/${encodeURIComponent(publicId)}`);
  }
}
