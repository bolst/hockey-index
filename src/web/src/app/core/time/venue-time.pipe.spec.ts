import { VenueTimePipe, formatVenueTime } from './venue-time.pipe';

describe('formatVenueTime', () => {
  describe('America/Toronto fall-back (2026-11-01 02:00 EDT -> 01:00 EST)', () => {
    it('shows the first 1:30 AM as EDT', () => {
      expect(formatVenueTime('2026-11-01T05:30:00Z', 'America/Toronto', 'time')).toBe('1:30 AM EDT');
    });

    it('shows the repeated 1:30 AM as EST', () => {
      expect(formatVenueTime('2026-11-01T06:30:00Z', 'America/Toronto', 'time')).toBe('1:30 AM EST');
    });

    it('keeps the venue date across the UTC midnight boundary', () => {
      expect(formatVenueTime('2026-11-01T03:30:00Z', 'America/Toronto', 'datetime')).toBe(
        'Sat, Oct 31, 2026, 11:30 PM EDT',
      );
    });
  });

  describe('America/Toronto spring-forward (2026-03-08 02:00 EST -> 03:00 EDT)', () => {
    it('shows the last minute before the gap as EST', () => {
      expect(formatVenueTime('2026-03-08T06:59:00Z', 'America/Toronto', 'time')).toBe('1:59 AM EST');
    });

    it('jumps straight to 3:00 AM EDT', () => {
      expect(formatVenueTime('2026-03-08T07:00:00Z', 'America/Toronto', 'time')).toBe('3:00 AM EDT');
    });
  });

  describe('America/Phoenix (no DST)', () => {
    it.each(['2026-03-08T07:00:00Z', '2026-07-01T07:00:00Z', '2026-11-01T07:00:00Z'])(
      'stays on MST at %s',
      (instant) => {
        expect(formatVenueTime(instant, 'America/Phoenix', 'time')).toBe('12:00 AM MST');
      },
    );

    it('differs from Toronto by 3 hours in summer and 2 hours in winter', () => {
      expect(formatVenueTime('2026-07-01T19:00:00Z', 'America/Phoenix', 'time')).toBe('12:00 PM MST');
      expect(formatVenueTime('2026-07-01T19:00:00Z', 'America/Toronto', 'time')).toBe('3:00 PM EDT');
      expect(formatVenueTime('2026-12-01T19:00:00Z', 'America/Phoenix', 'time')).toBe('12:00 PM MST');
      expect(formatVenueTime('2026-12-01T19:00:00Z', 'America/Toronto', 'time')).toBe('2:00 PM EST');
    });
  });

  it('formats the date only', () => {
    expect(formatVenueTime('2026-11-01T06:30:00Z', 'America/Toronto', 'date')).toBe('Sun, Nov 1, 2026');
  });

  it('returns an empty string for an invalid instant', () => {
    expect(formatVenueTime('not a date', 'America/Toronto')).toBe('');
  });
});

describe('VenueTimePipe', () => {
  const pipe = new VenueTimePipe();

  it('defaults to date and time', () => {
    expect(pipe.transform('2026-11-01T06:30:00Z', 'America/Toronto')).toBe('Sun, Nov 1, 2026, 1:30 AM EST');
  });

  it('renders nothing for a missing instant', () => {
    expect(pipe.transform(null, 'America/Toronto')).toBe('');
  });
});
