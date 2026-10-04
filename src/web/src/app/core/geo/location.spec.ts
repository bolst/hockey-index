import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { nearestCity, searchCities } from './cities';
import { LocationService } from './location';

describe('LocationService', () => {
  let controller: HttpTestingController;
  let service: LocationService;
  const original = Object.getOwnPropertyDescriptor(navigator, 'geolocation');

  function stubGeolocation(result: 'ok' | 'denied'): void {
    Object.defineProperty(navigator, 'geolocation', {
      configurable: true,
      value: {
        getCurrentPosition: (ok: PositionCallback, fail: PositionErrorCallback) =>
          result === 'ok'
            ? ok({ coords: { latitude: 43.6612, longitude: -79.3801 } } as GeolocationPosition)
            : fail({ code: 1 } as GeolocationPositionError),
      },
    });
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    controller = TestBed.inject(HttpTestingController);
    service = TestBed.inject(LocationService);
  });

  afterEach(() => {
    controller.verify();
    if (original) {
      Object.defineProperty(navigator, 'geolocation', original);
    } else {
      delete (navigator as { geolocation?: unknown }).geolocation;
    }
  });

  it('uses the device position first and skips the IP lookup', async () => {
    stubGeolocation('ok');
    expect(await service.locate()).toEqual({ lat: 43.6612, lng: -79.3801, source: 'device' });
  });

  it('falls back to /geo/ip when geolocation is denied', async () => {
    stubGeolocation('denied');
    const result = service.locate();
    await vi.waitFor(() => controller.expectOne(`${environment.apiBaseUrl}/geo/ip`).flush({ latitude: 45.5, longitude: -73.57 }));
    expect(await result).toEqual({ lat: 45.5, lng: -73.57, source: 'network' });
  });

  it('resolves null when both lookups fail', async () => {
    stubGeolocation('denied');
    const result = service.locate();
    await vi.waitFor(() => controller.expectOne(`${environment.apiBaseUrl}/geo/ip`).flush({ latitude: null, longitude: null }));
    expect(await result).toBeNull();
  });
});

describe('cities', () => {
  it('matches by prefix, ignoring accents and case', () => {
    expect(searchCities('montre')[0].name).toBe('Montréal');
    expect(searchCities('TOR')[0].name).toBe('Toronto');
  });

  it('labels a nearby coordinate with the nearest city', () => {
    expect(nearestCity(43.66, -79.38)?.name).toBe('Toronto');
    expect(nearestCity(0, 0)).toBeNull();
  });
});
