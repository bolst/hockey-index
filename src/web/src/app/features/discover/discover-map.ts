import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { Router } from '@angular/router';
import type * as Leaflet from 'leaflet';
import { SearchResult } from '../../core/api/discovery-api';
import { canonicalEventPath } from '../../../shared-render/event-summary';
import { Coordinates } from './search-filters';

/** OpenStreetMap standard tiles. Their usage policy requires this attribution and a Referer header. */
const OSM_TILE_URL = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png';
const OSM_ATTRIBUTION =
  '<a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">© OpenStreetMap contributors</a>';
const OSM_MAX_ZOOM = 19;
const LEAFLET_STYLESHEET = 'leaflet.css';
const MILES_TO_METERS = 1609.344;
const MARKER_SIZE = 22;
const HIGHLIGHT_Z_OFFSET = 1000;

type LeafletModule = typeof Leaflet;

/** leaflet ships UMD; production builds expose its API only on the interop `default` export. */
async function loadLeaflet(): Promise<LeafletModule> {
  const module = await import('leaflet');
  return ((module as { default?: unknown }).default ?? module) as LeafletModule;
}

/**
 * Only Ctrl+wheel or Cmd+wheel zooms, so the map never captures page scrolling.
 * Trackpad pinches arrive as Ctrl+wheel, so they zoom the map too.
 */
function passPlainWheelToPage(event: WheelEvent): void {
  if (!event.ctrlKey && !event.metaKey) {
    event.stopPropagation();
  }
}

interface ResultMarker {
  marker: Leaflet.Marker;
  button: HTMLButtonElement;
}

/**
 * Results map. Leaflet loads on first render only, never in the initial bundle.
 * Markers and popups are built with DOM APIs and `textContent`, so event text never becomes HTML.
 */
@Component({
  selector: 'app-discover-map',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { role: 'region', 'aria-label': 'Map of results' },
  template: `
    <div #canvas class="canvas"></div>
    @if (failed()) {
      <p class="fallback" role="status">The map could not load. The list shows every result.</p>
    }
  `,
  styles: `
    :host {
      position: relative;
      display: block;
      overflow: hidden;
      border-radius: var(--mat-sys-corner-extra-large);
      background: var(--mat-sys-surface-container-high);
    }

    .canvas {
      position: absolute;
      inset: 0;
    }

    .fallback {
      position: absolute;
      inset: auto 16px 16px;
      margin: 0;
      padding: 12px 16px;
      border-radius: var(--mat-sys-corner-medium);
      background: var(--mat-sys-surface-container-highest);
      font: var(--mat-sys-body-medium);
    }

    :host ::ng-deep {
      .canvas {
        background: var(--mat-sys-surface-container-high);
        font: var(--mat-sys-body-medium);
      }

      .canvas:focus-visible {
        outline: 3px solid var(--mat-sys-primary);
        outline-offset: -3px;
      }

      .hi-marker {
        display: block;
        width: 22px;
        height: 22px;
        padding: 0;
        border: 3px solid var(--mat-sys-surface);
        border-radius: 50%;
        background: var(--mat-sys-primary);
        box-shadow: var(--mat-sys-level2);
        cursor: pointer;
        transition: transform 150ms cubic-bezier(0.2, 0, 0, 1), background-color 150ms;
      }

      .hi-marker:focus-visible {
        outline: 3px solid var(--mat-sys-tertiary);
        outline-offset: 2px;
      }

      .hi-marker.cancelled {
        background: var(--mat-sys-outline);
      }

      .hi-marker.highlighted {
        background: var(--mat-sys-tertiary);
        transform: scale(1.35);
      }

      .hi-origin {
        width: 16px;
        height: 16px;
        border: 3px solid var(--mat-sys-surface);
        border-radius: 50%;
        background: var(--mat-sys-on-surface);
        box-shadow: 0 0 0 6px color-mix(in srgb, var(--mat-sys-on-surface) 18%, transparent);
      }

      .hi-radius {
        fill: var(--mat-sys-primary);
        fill-opacity: 0.08;
        stroke: var(--mat-sys-primary);
        stroke-width: 1.5px;
        stroke-dasharray: 3 3;
      }

      /* OSM raster tiles are light-only; invert the base map alone, never markers or popups. */
      @media (prefers-color-scheme: dark) {
        .leaflet-tile-pane {
          filter: invert(1) hue-rotate(180deg) brightness(0.85) contrast(0.85) saturate(0.6);
        }
      }

      .canvas .leaflet-popup-content-wrapper,
      .canvas .leaflet-popup-tip {
        background: var(--mat-sys-surface-container-highest);
        color: var(--mat-sys-on-surface);
        box-shadow: var(--mat-sys-level2);
      }

      .canvas .leaflet-popup-content-wrapper {
        padding: 0;
        border-radius: var(--mat-sys-corner-medium);
      }

      .canvas .leaflet-popup-content {
        margin: 12px 16px;
        font: var(--mat-sys-body-medium);
      }

      .canvas .leaflet-popup-content a {
        color: var(--mat-sys-primary);
        font: var(--mat-sys-title-small);
      }

      .canvas .leaflet-control-attribution.leaflet-control {
        margin: 0 16px 8px 0;
        padding: 2px 8px;
        border-radius: var(--mat-sys-corner-small);
        background: color-mix(in srgb, var(--mat-sys-surface-container-highest) 85%, transparent);
        color: var(--mat-sys-on-surface-variant);
        font: var(--mat-sys-label-small);
      }

      .canvas .leaflet-control-attribution a {
        color: inherit;
      }

      .canvas .leaflet-bar.leaflet-control {
        overflow: hidden;
        margin: 16px 16px 0 0;
        border: none;
        border-radius: var(--mat-sys-corner-medium);
        background: var(--mat-sys-surface-container-highest);
        box-shadow: var(--mat-sys-level1);
      }

      .canvas .leaflet-bar a {
        width: 36px;
        height: 36px;
        border-radius: 0;
        border-bottom: 1px solid var(--mat-sys-outline-variant);
        background: transparent;
        color: var(--mat-sys-on-surface);
        font: 400 22px/36px var(--mat-sys-body-large-font);
      }

      .canvas .leaflet-bar a:last-child {
        border-bottom: none;
      }

      .canvas .leaflet-bar a:hover {
        background: color-mix(in srgb, var(--mat-sys-on-surface) 8%, transparent);
      }

      .canvas .leaflet-bar a.leaflet-disabled {
        background: transparent;
        color: color-mix(in srgb, var(--mat-sys-on-surface) 38%, transparent);
      }

      .canvas .leaflet-bar a:focus-visible {
        outline: 3px solid var(--mat-sys-primary);
        outline-offset: -3px;
      }
    }
  `,
})
export class DiscoverMap {
  readonly results = input.required<SearchResult[]>();
  readonly center = input.required<Coordinates>();
  readonly radiusMiles = input.required<number>();
  readonly highlightedId = input<string | null>(null);
  readonly markerSelected = output<string>();

  protected readonly failed = signal(false);
  private readonly canvas = viewChild.required<ElementRef<HTMLElement>>('canvas');
  private readonly document = inject(DOCUMENT);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly router = inject(Router);
  private readonly ready = signal(false);
  private lib: LeafletModule | null = null;
  private map: Leaflet.Map | null = null;
  private radius: Leaflet.Circle | null = null;
  private origin: Leaflet.Marker | null = null;
  private markerLayer: Leaflet.LayerGroup | null = null;
  private markers = new Map<string, ResultMarker>();
  private resizeObserver: ResizeObserver | null = null;

  constructor() {
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => void this.init());
    destroyRef.onDestroy(() => {
      this.resizeObserver?.disconnect();
      this.map?.remove();
    });

    effect(() => {
      const results = this.results();
      const center = this.center();
      const radius = this.radiusMiles();
      if (this.ready()) {
        untracked(() => this.draw(results, center, radius));
      }
    });

    effect(() => {
      const highlighted = this.highlightedId();
      if (this.ready()) {
        this.markers.forEach(({ marker, button }, id) => {
          const isHighlighted = id === highlighted;
          button.classList.toggle('highlighted', isHighlighted);
          marker.setZIndexOffset(isHighlighted ? HIGHLIGHT_Z_OFFSET : 0);
        });
      }
    });
  }

  private async init(): Promise<void> {
    try {
      const lib = await loadLeaflet();
      await this.ensureStylesheet(lib.version);
      this.lib = lib;
      const center = this.center();
      const container = this.canvas().nativeElement;
      this.map = lib.map(container, {
        center: [center.lat, center.lng],
        zoom: 10,
        maxZoom: OSM_MAX_ZOOM,
        zoomControl: false,
        attributionControl: false,
      });
      this.host.nativeElement.addEventListener('wheel', passPlainWheelToPage, { capture: true });
      lib
        .tileLayer(OSM_TILE_URL, { maxZoom: OSM_MAX_ZOOM, attribution: OSM_ATTRIBUTION })
        .addTo(this.map);
      lib.control.zoom({ position: 'topright' }).addTo(this.map);
      lib.control.attribution({ position: 'bottomright', prefix: false }).addTo(this.map);
      this.radius = lib
        .circle([center.lat, center.lng], {
          radius: this.radiusMiles() * MILES_TO_METERS,
          className: 'hi-radius',
          interactive: false,
        })
        .addTo(this.map);
      this.markerLayer = lib.layerGroup().addTo(this.map);
      this.resizeObserver = new ResizeObserver(() => this.map?.invalidateSize({ pan: false }));
      this.resizeObserver.observe(container);
      this.ready.set(true);
    } catch {
      this.failed.set(true);
    }
  }

  private draw(results: SearchResult[], center: Coordinates, radiusMiles: number): void {
    const lib = this.lib;
    const map = this.map;
    const markerLayer = this.markerLayer;
    if (!lib || !map || !markerLayer) {
      return;
    }
    const centerLatLng = lib.latLng(center.lat, center.lng);
    const radiusMeters = radiusMiles * MILES_TO_METERS;
    this.radius?.setLatLng(centerLatLng).setRadius(radiusMeters);

    this.origin?.remove();
    const originElement = this.document.createElement('div');
    originElement.className = 'hi-origin';
    originElement.setAttribute('aria-hidden', 'true');
    this.origin = lib
      .marker(centerLatLng, {
        icon: lib.divIcon({
          html: originElement,
          className: '',
          iconSize: [MARKER_SIZE, MARKER_SIZE],
        }),
        interactive: false,
        keyboard: false,
      })
      .addTo(map);

    markerLayer.clearLayers();
    this.markers.clear();
    const bounds = lib.latLngBounds(centerLatLng, centerLatLng);
    for (const result of results) {
      const position = lib.latLng(result.venue.latitude, result.venue.longitude);
      const button = this.document.createElement('button');
      button.type = 'button';
      button.className = result.status === 'cancelled' ? 'hi-marker cancelled' : 'hi-marker';
      button.setAttribute('aria-label', `${result.title}, ${result.venue.name}`);
      button.addEventListener('click', () => this.markerSelected.emit(result.publicId));
      // Leaflet turns Enter keypresses into a second click, closing the popup the native click opened.
      button.addEventListener('keypress', (event) => event.stopPropagation());
      // keyboard: false keeps the inner button as the only tab stop, not Leaflet's wrapper too.
      const marker = lib
        .marker(position, {
          icon: lib.divIcon({
            html: button,
            className: '',
            iconSize: [MARKER_SIZE, MARKER_SIZE],
            popupAnchor: [0, -MARKER_SIZE / 2],
          }),
          keyboard: false,
        })
        .bindPopup(this.popupContent(result), { closeButton: false })
        .addTo(markerLayer);
      this.markers.set(result.publicId, { marker, button });
      bounds.extend(position);
    }

    map.fitBounds(results.length ? bounds : centerLatLng.toBounds(radiusMeters * 2), {
      padding: [48, 48],
      maxZoom: 13,
      animate: false,
    });
  }

  private popupContent(result: SearchResult): HTMLElement {
    const container = this.document.createElement('div');
    const link = this.document.createElement('a');
    const path = canonicalEventPath(result.publicId, result.title);
    link.href = path;
    link.textContent = result.title;
    link.addEventListener('click', (event) => {
      event.preventDefault();
      void this.router.navigateByUrl(path);
    });
    const venue = this.document.createElement('div');
    venue.textContent = result.venue.name;
    container.append(link, venue);
    return container;
  }

  /**
   * The stylesheet name has no content hash, so the version query keeps immutable caching safe across upgrades.
   * The map MUST NOT be created before the stylesheet applies: Leaflet measures panes and controls on creation.
   */
  private ensureStylesheet(version: string): Promise<void> {
    const href = `${LEAFLET_STYLESHEET}?v=${encodeURIComponent(version)}`;
    const existing = this.document.head.querySelector<HTMLLinkElement>(`link[href="${href}"]`);
    if (existing?.sheet) {
      return Promise.resolve();
    }
    const link = existing ?? this.document.createElement('link');
    const loaded = new Promise<void>((resolve) => {
      link.addEventListener('load', () => resolve(), { once: true });
      link.addEventListener('error', () => resolve(), { once: true });
    });
    if (!existing) {
      link.rel = 'stylesheet';
      link.href = href;
      this.document.head.appendChild(link);
    }
    return loaded;
  }
}
