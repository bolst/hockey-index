/**
 * Port of the API's UrlExtractor (src/api/HockeyIndex.Api/Infrastructure/Text/UrlExtractor.cs).
 * The server scans exactly the URLs that extractor returns, so the linkifier MUST link the same set.
 */
export const MAX_URLS = 10;

export type UrlRejection = 'InvalidUrl' | 'UnsupportedScheme' | 'UserInfo' | 'NonDefaultPort';

export type NormalizeResult = { ok: true; url: string } | { ok: false; reason: UrlRejection };

export type TextSegment = { kind: 'text'; text: string } | { kind: 'link'; text: string; url: string };

const TRAILING_PUNCTUATION = '.,;:!?*';
const BRACKET_PAIRS: Record<string, string> = { ')': '(', ']': '[', '}': '{' };

/** Mirrors the .NET pattern; `\w` there is Unicode-aware, hence the explicit property classes. */
const CANDIDATE_PATTERN = /(?<![\p{L}\p{Mn}\p{Nd}\p{Pc}+.-])https?:\/\/[^\s<>"'`]+/giu;

function count(text: string, char: string): number {
  return text.split(char).length - 1;
}

function trimTrailing(candidate: string): string {
  while (candidate.length > 0) {
    const last = candidate[candidate.length - 1];
    const open = BRACKET_PAIRS[last];
    if (TRAILING_PUNCTUATION.includes(last) || (open !== undefined && count(candidate, last) > count(candidate, open))) {
      candidate = candidate.slice(0, -1);
    } else {
      break;
    }
  }
  return candidate;
}

function authorityOf(candidate: string): string {
  const start = candidate.indexOf('://');
  if (start < 0) {
    return '';
  }
  const authority = candidate.slice(start + 3);
  const end = authority.search(/[/?#\\]/);
  return end < 0 ? authority : authority.slice(0, end);
}

/** Canonical form: lowercase scheme and punycode host, no default port, no fragment. */
export function normalizeUrl(candidate: string): NormalizeResult {
  let parsed: URL;
  try {
    parsed = new URL(candidate);
  } catch {
    return { ok: false, reason: 'InvalidUrl' };
  }
  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
    return { ok: false, reason: 'UnsupportedScheme' };
  }
  const authority = authorityOf(candidate);
  if (authority.length === 0) {
    return { ok: false, reason: 'InvalidUrl' };
  }
  if (parsed.username || parsed.password || authority.includes('@')) {
    return { ok: false, reason: 'UserInfo' };
  }
  if (parsed.port !== '') {
    return { ok: false, reason: 'NonDefaultPort' };
  }
  const host = parsed.hostname.replace(/\.+$/, '');
  if (host.length === 0) {
    return { ok: false, reason: 'InvalidUrl' };
  }
  return { ok: true, url: `${parsed.protocol}//${host}${parsed.pathname}${parsed.search}` };
}

/**
 * Splits text into plain and link segments. Links get the URL as written (trailing punctuation
 * removed) as their text and the canonical URL for the outbound check. At most MAX_URLS distinct
 * URLs are linked; repeats of an accepted URL are linked too.
 */
export function segmentText(text: string): TextSegment[] {
  const segments: TextSegment[] = [];
  const accepted = new Set<string>();
  let plain = '';
  let cursor = 0;

  for (const match of text.matchAll(CANDIDATE_PATTERN)) {
    const candidate = trimTrailing(match[0]);
    const normalized = normalizeUrl(candidate);
    const canLink =
      normalized.ok && (accepted.has(normalized.url) || accepted.size < MAX_URLS);
    if (!normalized.ok || !canLink) {
      continue;
    }
    accepted.add(normalized.url);
    const start = match.index;
    plain += text.slice(cursor, start);
    if (plain) {
      segments.push({ kind: 'text', text: plain });
      plain = '';
    }
    segments.push({ kind: 'link', text: candidate, url: normalized.url });
    cursor = start + candidate.length;
  }

  plain += text.slice(cursor);
  if (plain) {
    segments.push({ kind: 'text', text: plain });
  }
  return segments;
}

export function outboundHref(url: string, eventId: string): string {
  return `/out?u=${encodeURIComponent(url)}&e=${encodeURIComponent(eventId)}`;
}
