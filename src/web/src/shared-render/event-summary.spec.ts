import {
  EventSummaryInput,
  PublicEvent,
  canonicalEventPath,
  escapeHtml,
  eventSlug,
  feeLabel,
  jsonForScript,
  renderEventHead,
  renderEventSummary,
  skillLabel,
  whenLabel,
} from './event-summary';

const baseEvent: EventSummaryInput = {
  title: 'Sunday Stick and Puck',
  description: 'Bring water.',
  venueName: 'Maple Arena',
  venueCity: 'Toronto',
  venueRegion: 'ON',
  startsAtLocal: '2026-11-01 19:00',
  skillLabel: 'Intermediate',
  feeLabel: '$15',
};

describe('renderEventSummary', () => {
  it('escapes a script tag in the title', () => {
    const html = renderEventSummary({ ...baseEvent, title: '<script>alert(1)</script>' });
    expect(html).not.toContain('<script>');
    expect(html).toContain('&lt;script&gt;alert(1)&lt;/script&gt;');
  });

  it('escapes every field', () => {
    const payload = '<img src=x onerror="a&b">\'';
    const html = renderEventSummary({
      title: payload,
      description: payload,
      venueName: payload,
      venueCity: payload,
      venueRegion: payload,
      startsAtLocal: payload,
      skillLabel: payload,
      feeLabel: payload,
    });
    expect(html).not.toMatch(/<img/);
    expect(html).not.toContain('onerror="');
    expect(html.match(/&lt;img src=x onerror=&quot;a&amp;b&quot;&gt;&#39;/g)).toHaveLength(8);
  });

  it('omits optional fields that are empty', () => {
    const html = renderEventSummary({ ...baseEvent, description: null, skillLabel: null });
    expect(html).not.toContain('<p>Bring water.</p>');
    expect(html).not.toContain('Intermediate');
  });
});

describe('escapeHtml', () => {
  it('escapes the five HTML metacharacters', () => {
    expect(escapeHtml(`&<>"'`)).toBe('&amp;&lt;&gt;&quot;&#39;');
  });
});

const publicEvent: PublicEvent = {
  publicId: 'abcdefgh23',
  status: 'published',
  type: 'scrimmage',
  title: 'Sunday Night Skate',
  description: null,
  venue: {
    publicId: 'venue23456',
    name: 'Maple Arena',
    addressLine: '1 Rink Rd',
    city: 'Toronto',
    region: 'ON',
    country: 'CA',
    timeZone: 'America/Toronto',
    latitude: 43.66,
    longitude: -79.38,
  },
  startsLocal: '2026-10-10T19:00:00',
  endsLocal: '2026-10-10T20:30:00',
  startsAt: '2026-10-10T19:00:00-04:00',
  endsAt: '2026-10-10T20:30:00-04:00',
  skill: { min: 2, max: 4 },
  feeCents: 1500,
  currency: 'CAD',
  updatedAt: '2026-10-01T12:00:00Z',
};

describe('eventSlug', () => {
  // Same fixtures as EventSlugTests in the API; the two implementations MUST agree.
  it.each([
    ['Sunday Night Skate', 'sunday-night-skate'],
    ['  Beer League -- Tier 2!  ', 'beer-league-tier-2'],
    ['Équipe Montréal Hockey', 'equipe-montreal-hockey'],
    ['Ünïcödé Ïcé', 'unicode-ice'],
    ['日本語', ''],
    ['a'.repeat(59) + ' b', 'a'.repeat(59)],
    ['x'.repeat(70), 'x'.repeat(60)],
  ])('slugs %j as %j', (title, slug) => {
    expect(eventSlug(title)).toBe(slug);
  });

  it('omits an empty slug from the canonical path', () => {
    expect(canonicalEventPath('abcdefgh23', '日本語')).toBe('/e/abcdefgh23');
    expect(canonicalEventPath('abcdefgh23', 'Skate')).toBe('/e/abcdefgh23/skate');
  });
});

describe('labels', () => {
  it('formats skill and fee', () => {
    expect(skillLabel({ min: 2, max: 4 })).toBe('Skill 2\u20134');
    expect(skillLabel({ min: 3, max: 3 })).toBe('Skill 3');
    expect(feeLabel(0, 'CAD')).toBe('Free');
    expect(feeLabel(1500, 'CAD')).toBe('$15 CAD');
    expect(feeLabel(1250, 'USD')).toBe('$12.50 USD');
    expect(feeLabel(null, 'CAD')).toBe('Fee: see join instructions');
  });

  it('formats a scrimmage in venue-local time with the zone abbreviation', () => {
    expect(whenLabel(publicEvent)).toBe('Sat, Oct 10, 2026, 7:00 PM EDT');
  });

  it('formats a season as a date range with its schedule', () => {
    expect(
      whenLabel({ ...publicEvent, type: 'league', startDate: '2026-10-01', endDate: '2027-03-31', scheduleText: 'Tuesdays' }),
    ).toBe('Oct 1, 2026 \u2013 Mar 31, 2027 \u00b7 Tuesdays');
  });
});

describe('renderEventHead', () => {
  it('escapes JSON for a script block', () => {
    expect(jsonForScript({ a: '</script>&\u2028' })).toBe('{"a":"\\u003c/script\\u003e\\u0026\\u2028"}');
  });

  it('escapes attribute values and adds noindex only when archived', () => {
    const head = renderEventHead({ ...publicEvent, title: '"><b>x' }, 'https://hockeyindex.com/e/abcdefgh23/b-x');
    expect(head).toContain('<meta property="og:title" content="&quot;&gt;&lt;b&gt;x">');
    expect(head).not.toContain('noindex');
    expect(renderEventHead({ ...publicEvent, status: 'archived' }, 'u')).toContain('<meta name="robots" content="noindex">');
  });
});
