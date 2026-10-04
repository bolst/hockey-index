import { Pipe, PipeTransform } from '@angular/core';

export type VenueTimeStyle = 'datetime' | 'date' | 'time';

const STYLE_OPTIONS: Record<VenueTimeStyle, Intl.DateTimeFormatOptions> = {
  datetime: {
    weekday: 'short',
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
    timeZoneName: 'short',
  },
  date: { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' },
  time: { hour: 'numeric', minute: '2-digit', timeZoneName: 'short' },
};

const NARROW_NO_BREAK_SPACE = /\u202f/g;

const formatters = new Map<string, Intl.DateTimeFormat>();

function formatterFor(timeZone: string, style: VenueTimeStyle, locale: string): Intl.DateTimeFormat {
  const key = `${locale}|${timeZone}|${style}`;
  let formatter = formatters.get(key);
  if (!formatter) {
    formatter = new Intl.DateTimeFormat(locale, { ...STYLE_OPTIONS[style], timeZone });
    formatters.set(key, formatter);
  }
  return formatter;
}

/** Formats a UTC instant as wall time at the venue, whatever the viewer's own time zone is. */
export function formatVenueTime(
  instant: string | number | Date,
  timeZone: string,
  style: VenueTimeStyle = 'datetime',
  locale = 'en-US',
): string {
  const date = instant instanceof Date ? instant : new Date(instant);
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  return formatterFor(timeZone, style, locale).format(date).replace(NARROW_NO_BREAK_SPACE, ' ');
}

@Pipe({ name: 'venueTime' })
export class VenueTimePipe implements PipeTransform {
  transform(
    instant: string | number | Date | null | undefined,
    timeZone: string,
    style: VenueTimeStyle = 'datetime',
  ): string {
    return instant === null || instant === undefined ? '' : formatVenueTime(instant, timeZone, style);
  }
}
