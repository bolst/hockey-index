import {
  DEFAULT_FILTERS,
  activeFilterCount,
  buildSearchQuery,
  coordinatesFromParams,
  filtersFromParams,
  roundCoordinate,
  toQueryParams,
  utcHour,
} from './search-filters';

describe('search filters', () => {
  it('rounds coordinates to 2 decimal places without negative zero', () => {
    expect(roundCoordinate(43.66123)).toBe('43.66');
    expect(roundCoordinate(-79.38499)).toBe('-79.38');
    expect(roundCoordinate(-0.001)).toBe('0');
    expect(roundCoordinate(45.5)).toBe('45.5');
  });

  it('truncates `from` to the start of the UTC hour', () => {
    expect(utcHour(new Date('2026-10-03T23:59:41.512Z'))).toBe('2026-10-03T23:00:00Z');
  });

  it('builds the canonical search query with defaults only', () => {
    const query = buildSearchQuery({ lat: 43.6612, lng: -79.3801 }, DEFAULT_FILTERS, new Date('2026-10-03T14:20:00Z'));
    expect(query.toString()).toBe('lat=43.66&lng=-79.38&r=25&from=2026-10-03T14:00:00Z&days=14');
  });

  it('adds skill, type and fee only when narrowed', () => {
    const query = buildSearchQuery(
      { lat: 43.66, lng: -79.38 },
      { ...DEFAULT_FILTERS, skillMin: 2, skillMax: 4, type: 'league', maxFee: 0 },
      new Date('2026-10-03T14:20:00Z'),
    );
    expect(query.toString()).toContain('&smin=2&smax=4&type=league&maxFee=0');
  });

  it('reads URL params and ignores invalid values', () => {
    expect(filtersFromParams({ r: '50', days: '7', smin: '3', smax: '5', type: 'tournament', maxFee: '1500' })).toEqual({
      radius: 50,
      days: 7,
      skillMin: 3,
      skillMax: 5,
      type: 'tournament',
      maxFee: 1500,
    });
    expect(filtersFromParams({ r: '12', days: 'x', smin: '6', smax: '2', type: 'pond', maxFee: '999' })).toEqual(
      DEFAULT_FILTERS,
    );
    expect(coordinatesFromParams({ lat: '43.66', lng: '-79.38' })).toEqual({ lat: 43.66, lng: -79.38 });
    expect(coordinatesFromParams({ lat: '95', lng: '0' })).toBeNull();
  });

  it('writes short URLs that round-trip', () => {
    const filters = { ...DEFAULT_FILTERS, radius: 10 as const, maxFee: 2000 as const };
    const params = toQueryParams({ lat: 43.66123, lng: -79.38 }, filters);
    expect(params).toEqual({ lat: '43.66', lng: '-79.38', r: 10, days: null, smin: null, smax: null, type: null, maxFee: 2000 });
    const asStrings = Object.fromEntries(Object.entries(params).filter(([, v]) => v !== null).map(([k, v]) => [k, String(v)]));
    expect(filtersFromParams(asStrings)).toEqual(filters);
    expect(activeFilterCount(filters)).toBe(2);
  });
});
