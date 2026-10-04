import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { DiscoveryApi } from '../api/discovery-api';

export type LocationSource = 'device' | 'network' | 'manual' | 'link';

export interface SearchLocation {
  lat: number;
  lng: number;
  source: LocationSource;
  label?: string;
}

const GEOLOCATION_TIMEOUT_MS = 8000;
const GEOLOCATION_MAX_AGE_MS = 10 * 60 * 1000;

/**
 * Finds where to search: the browser's geolocation first, then the API's IP lookup. Resolves `null`
 * when both fail so the page can ask for a place by name.
 */
@Injectable({ providedIn: 'root' })
export class LocationService {
  private readonly api = inject(DiscoveryApi);
  private readonly window = inject(DOCUMENT).defaultView;

  async locate(): Promise<SearchLocation | null> {
    return (await this.fromDevice()) ?? (await this.fromNetwork());
  }

  fromDevice(): Promise<SearchLocation | null> {
    const geolocation = this.window?.navigator?.geolocation;
    if (!geolocation) {
      return Promise.resolve(null);
    }
    return new Promise((resolve) => {
      geolocation.getCurrentPosition(
        (position) => resolve({ lat: position.coords.latitude, lng: position.coords.longitude, source: 'device' }),
        () => resolve(null),
        { enableHighAccuracy: false, timeout: GEOLOCATION_TIMEOUT_MS, maximumAge: GEOLOCATION_MAX_AGE_MS },
      );
    });
  }

  async fromNetwork(): Promise<SearchLocation | null> {
    try {
      const { latitude, longitude } = await firstValueFrom(this.api.ipLocation());
      return latitude === null || longitude === null ? null : { lat: latitude, lng: longitude, source: 'network' };
    } catch {
      return null;
    }
  }
}
