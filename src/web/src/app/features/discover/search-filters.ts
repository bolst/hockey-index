import { HttpParams } from '@angular/common/http';
import { Params } from '@angular/router';
import { EventType } from '../../core/api/discovery-api';

export const RADIUS_OPTIONS = [5, 10, 25, 50, 100] as const;
export const DAYS_OPTIONS = [1, 3, 7, 14, 30] as const;
export const MAX_FEE_OPTIONS = [0, 1000, 1500, 2000, 2500, 3000, 5000] as const;
export const EVENT_TYPES: readonly EventType[] = ['scrimmage', 'league', 'tournament'];
export const SKILL_MIN = 0;
export const SKILL_MAX = 7;

export type Radius = (typeof RADIUS_OPTIONS)[number];
export type Days = (typeof DAYS_OPTIONS)[number];
export type MaxFee = (typeof MAX_FEE_OPTIONS)[number];

export interface SearchFilters {
  radius: Radius;
  days: Days;
  skillMin: number;
  skillMax: number;
  type: EventType | null;
  maxFee: MaxFee | null;
}

export interface Coordinates {
  lat: number;
  lng: number;
}

export const DEFAULT_FILTERS: SearchFilters = {
  radius: 25,
  days: 14,
  skillMin: SKILL_MIN,
  skillMax: SKILL_MAX,
  type: null,
  maxFee: null,
};

/** Rounds to 2 decimal places (about 1 km), the precision the API accepts and caches on. */
export function roundCoordinate(value: number): string {
  const rounded = Math.round(value * 100) / 100;
  return String(Object.is(rounded, -0) ? 0 : rounded);
}

/** Start of the current UTC hour, `yyyy-MM-ddTHH:00:00Z`, so searches within an hour share a cache key. */
export function utcHour(now: Date): string {
  return `${now.toISOString().slice(0, 13)}:00:00Z`;
}

/** Canonical `/v1/search` parameters, always in the same order. */
export function buildSearchQuery(where: Coordinates, filters: SearchFilters, now: Date): HttpParams {
  let params = new HttpParams()
    .set('lat', roundCoordinate(where.lat))
    .set('lng', roundCoordinate(where.lng))
    .set('r', filters.radius)
    .set('from', utcHour(now))
    .set('days', filters.days);
  if (filters.skillMin > SKILL_MIN || filters.skillMax < SKILL_MAX) {
    params = params.set('smin', filters.skillMin).set('smax', filters.skillMax);
  }
  if (filters.type) {
    params = params.set('type', filters.type);
  }
  if (filters.maxFee !== null) {
    params = params.set('maxFee', filters.maxFee);
  }
  return params;
}

function pick<T extends number>(raw: unknown, allowed: readonly T[]): T | null {
  const value = Number(raw);
  return raw !== undefined && raw !== null && raw !== '' && allowed.includes(value as T) ? (value as T) : null;
}

function skill(raw: unknown): number | null {
  const value = Number(raw);
  return typeof raw === 'string' && /^[0-7]$/.test(raw) ? value : null;
}

function coordinate(raw: unknown, limit: number): number | null {
  if (typeof raw !== 'string' || raw.trim() === '') {
    return null;
  }
  const value = Number(raw);
  return Number.isFinite(value) && Math.abs(value) <= limit ? value : null;
}

/** Reads the page's URL query params, ignoring anything invalid. */
export function filtersFromParams(params: Params): SearchFilters {
  let skillMin = skill(params['smin']) ?? SKILL_MIN;
  let skillMax = skill(params['smax']) ?? SKILL_MAX;
  if (skillMin > skillMax) {
    [skillMin, skillMax] = [SKILL_MIN, SKILL_MAX];
  }
  const type = params['type'];
  return {
    radius: pick(params['r'], RADIUS_OPTIONS) ?? DEFAULT_FILTERS.radius,
    days: pick(params['days'], DAYS_OPTIONS) ?? DEFAULT_FILTERS.days,
    skillMin,
    skillMax,
    type: EVENT_TYPES.includes(type) ? (type as EventType) : null,
    maxFee: pick(params['maxFee'], MAX_FEE_OPTIONS),
  };
}

export function coordinatesFromParams(params: Params): Coordinates | null {
  const lat = coordinate(params['lat'], 90);
  const lng = coordinate(params['lng'], 180);
  return lat === null || lng === null ? null : { lat, lng };
}

/** Query params for the page URL. Defaults are left out so shared links stay short. */
export function toQueryParams(where: Coordinates | null, filters: SearchFilters): Params {
  const narrowedSkill = filters.skillMin > SKILL_MIN || filters.skillMax < SKILL_MAX;
  return {
    lat: where ? roundCoordinate(where.lat) : null,
    lng: where ? roundCoordinate(where.lng) : null,
    r: filters.radius === DEFAULT_FILTERS.radius ? null : filters.radius,
    days: filters.days === DEFAULT_FILTERS.days ? null : filters.days,
    smin: narrowedSkill ? filters.skillMin : null,
    smax: narrowedSkill ? filters.skillMax : null,
    type: filters.type,
    maxFee: filters.maxFee,
  };
}

export function activeFilterCount(filters: SearchFilters): number {
  return [
    filters.radius !== DEFAULT_FILTERS.radius,
    filters.days !== DEFAULT_FILTERS.days,
    filters.skillMin > SKILL_MIN || filters.skillMax < SKILL_MAX,
    filters.type !== null,
    filters.maxFee !== null,
  ].filter(Boolean).length;
}

export function maxFeeLabel(maxFee: MaxFee | null): string {
  if (maxFee === null) {
    return 'Any price';
  }
  return maxFee === 0 ? 'Free only' : `Up to $${maxFee / 100}`;
}

export function daysLabel(days: Days): string {
  return days === 1 ? 'Next 24 hours' : `Next ${days} days`;
}

export function typeLabel(type: EventType): string {
  return { scrimmage: 'Scrimmage', league: 'League', tournament: 'Tournament' }[type];
}
