export interface EventSummaryInput {
  title: string;
  description?: string | null;
  venueName: string;
  venueCity: string;
  venueRegion: string;
  startsAtLocal: string;
  skillLabel?: string | null;
  feeLabel?: string | null;
  statusLabel?: string | null;
}

const HTML_ESCAPES: Readonly<Record<string, string>> = {
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
  "'": '&#39;',
};

export function escapeHtml(value: unknown): string {
  return String(value ?? '').replace(/[&<>"']/g, (character) => HTML_ESCAPES[character]);
}

export function renderEventSummary(event: EventSummaryInput): string {
  const details = [event.startsAtLocal, event.skillLabel, event.feeLabel]
    .filter((detail): detail is string => !!detail)
    .map((detail) => `<li>${escapeHtml(detail)}</li>`)
    .join('');
  const description = event.description ? `<p>${escapeHtml(event.description)}</p>` : '';
  const status = event.statusLabel ? `<p><strong>${escapeHtml(event.statusLabel)}</strong></p>` : '';
  return (
    `<article><h1>${escapeHtml(event.title)}</h1>${status}` +
    `<p>${escapeHtml(event.venueName)}, ${escapeHtml(event.venueCity)}, ${escapeHtml(event.venueRegion)}</p>` +
    `<ul>${details}</ul>${description}</article>`
  );
}

/** Shape of `GET /v1/events/{publicId}` (camelCase JSON from the API). */
export interface PublicEventVenue {
  publicId: string;
  name: string;
  addressLine: string;
  city: string;
  region: string;
  country: string;
  timeZone: string;
  latitude: number;
  longitude: number;
}

export interface PublicEvent {
  publicId: string;
  status: 'published' | 'cancelled' | 'archived';
  type: string;
  title: string;
  description?: string | null;
  venue: PublicEventVenue;
  rinkLabel?: string | null;
  startsLocal: string;
  endsLocal: string;
  startsAt: string;
  endsAt: string;
  startDate?: string | null;
  endDate?: string | null;
  scheduleText?: string | null;
  skill: { min: number; max: number };
  feeCents?: number | null;
  currency: string;
  joinInstructions?: string | null;
  publishedAt?: string | null;
  cancelledAt?: string | null;
  updatedAt: string;
}

export const SITE_NAME = 'Hockey Index';
export const PUBLIC_ID_PATTERN = /^[a-z2-7]{10}$/;
const MAX_SLUG_LENGTH = 60;
const MAX_META_DESCRIPTION_LENGTH = 160;

/** MUST match `EventSlug.From` in the API (Features/Discovery/EventSlug.cs). */
export function eventSlug(title: string): string {
  return title
    .normalize('NFKD')
    .replace(/\p{Mn}/gu, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, MAX_SLUG_LENGTH)
    .replace(/-+$/, '');
}

export function canonicalEventPath(publicId: string, title: string): string {
  const slug = eventSlug(title);
  return slug ? `/e/${publicId}/${slug}` : `/e/${publicId}`;
}

export function skillLabel(skill: { min: number; max: number }): string {
  return skill.min === skill.max ? `Skill ${skill.min}` : `Skill ${skill.min}\u2013${skill.max}`;
}

export function feeLabel(feeCents: number | null | undefined, currency: string): string {
  if (feeCents === null || feeCents === undefined) {
    return 'Fee: see join instructions';
  }
  if (feeCents === 0) {
    return 'Free';
  }
  const amount = feeCents % 100 === 0 ? String(feeCents / 100) : (feeCents / 100).toFixed(2);
  return `$${amount} ${currency}`;
}

function plainSpaces(text: string): string {
  return text.replace(/[\u202f\u2009\u00a0]/g, ' ');
}

function formatLocalDate(isoDate: string): string {
  const [year, month, day] = isoDate.slice(0, 10).split('-').map(Number);
  return plainSpaces(
    new Intl.DateTimeFormat('en-US', { timeZone: 'UTC', month: 'short', day: 'numeric', year: 'numeric' }).format(
      new Date(Date.UTC(year, month - 1, day)),
    ),
  );
}

/** Venue-local wording: one start time for a scrimmage, a date range (plus schedule) for a season. */
export function whenLabel(event: PublicEvent): string {
  if (event.startDate && event.endDate) {
    const range = `${formatLocalDate(event.startDate)} \u2013 ${formatLocalDate(event.endDate)}`;
    return event.scheduleText ? `${range} \u00b7 ${event.scheduleText}` : range;
  }
  return plainSpaces(
    new Intl.DateTimeFormat('en-US', {
      timeZone: event.venue.timeZone,
      weekday: 'short',
      month: 'short',
      day: 'numeric',
      year: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
      timeZoneName: 'short',
    }).format(new Date(event.startsAt)),
  );
}

export function toSummaryInput(event: PublicEvent): EventSummaryInput {
  return {
    title: event.title,
    description: event.description,
    venueName: event.venue.name,
    venueCity: event.venue.city,
    venueRegion: event.venue.region,
    startsAtLocal: whenLabel(event),
    skillLabel: skillLabel(event.skill),
    feeLabel: feeLabel(event.feeCents, event.currency),
    statusLabel: event.status === 'cancelled' ? 'Cancelled' : null,
  };
}

export function metaDescription(event: PublicEvent): string {
  const text = [
    event.status === 'cancelled' ? 'Cancelled.' : '',
    `${whenLabel(event)} at ${event.venue.name}, ${event.venue.city}, ${event.venue.region}.`,
    `${skillLabel(event.skill)}. ${feeLabel(event.feeCents, event.currency)}.`,
  ]
    .filter((part) => part)
    .join(' ');
  return text.length <= MAX_META_DESCRIPTION_LENGTH ? text : `${text.slice(0, MAX_META_DESCRIPTION_LENGTH - 1)}\u2026`;
}

/** Serializes JSON for an inline `<script>` data block: no `</script>`, HTML comments or JS line terminators survive. */
export function jsonForScript(value: unknown): string {
  return JSON.stringify(value).replace(
    /[<>&\u2028\u2029]/g,
    (character) => `\\u${character.charCodeAt(0).toString(16).padStart(4, '0')}`,
  );
}

export function eventJsonLd(event: PublicEvent, canonicalUrl: string): Record<string, unknown> {
  const jsonLd: Record<string, unknown> = {
    '@context': 'https://schema.org',
    '@type': 'SportsEvent',
    name: event.title,
    url: canonicalUrl,
    sport: 'Ice hockey',
    startDate: event.startsAt,
    endDate: event.endsAt,
    eventStatus:
      event.status === 'cancelled' ? 'https://schema.org/EventCancelled' : 'https://schema.org/EventScheduled',
    eventAttendanceMode: 'https://schema.org/OfflineEventAttendanceMode',
    location: {
      '@type': 'Place',
      name: event.venue.name,
      address: {
        '@type': 'PostalAddress',
        streetAddress: event.venue.addressLine,
        addressLocality: event.venue.city,
        addressRegion: event.venue.region,
        addressCountry: event.venue.country,
      },
      geo: { '@type': 'GeoCoordinates', latitude: event.venue.latitude, longitude: event.venue.longitude },
    },
  };
  if (event.description) {
    jsonLd['description'] = event.description;
  }
  if (event.feeCents !== null && event.feeCents !== undefined) {
    jsonLd['offers'] = {
      '@type': 'Offer',
      price: (event.feeCents / 100).toFixed(2),
      priceCurrency: event.currency,
      url: canonicalUrl,
    };
  }
  return jsonLd;
}

export function documentTitle(event: PublicEvent): string {
  return `${event.title} \u00b7 ${SITE_NAME}`;
}

/** Tags inserted before `</head>`; every value is HTML-escaped and the JSON-LD is script-escaped. */
export function renderEventHead(event: PublicEvent, canonicalUrl: string): string {
  const description = metaDescription(event);
  const tags = [
    `<meta name="description" content="${escapeHtml(description)}">`,
    `<link rel="canonical" href="${escapeHtml(canonicalUrl)}">`,
    `<meta property="og:type" content="website">`,
    `<meta property="og:site_name" content="${SITE_NAME}">`,
    `<meta property="og:title" content="${escapeHtml(event.title)}">`,
    `<meta property="og:description" content="${escapeHtml(description)}">`,
    `<meta property="og:url" content="${escapeHtml(canonicalUrl)}">`,
    `<meta name="twitter:card" content="summary">`,
    event.status === 'archived' ? `<meta name="robots" content="noindex">` : '',
    `<script type="application/ld+json">${jsonForScript(eventJsonLd(event, canonicalUrl))}</script>`,
  ];
  return tags.filter((tag) => tag).join('');
}
