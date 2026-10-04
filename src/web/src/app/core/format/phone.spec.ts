import { formatPhone } from './phone';

describe('formatPhone', () => {
  it('formats NANP numbers as +1 NXX-NXX-XXXX', () => {
    expect(formatPhone('+14165550142')).toBe('+1 416-555-0142');
    expect(formatPhone('+12065550100')).toBe('+1 206-555-0100');
  });

  it('leaves other numbers in E.164', () => {
    expect(formatPhone('+442071838750')).toBe('+442071838750');
  });

  it('returns an empty string for missing values', () => {
    expect(formatPhone(null)).toBe('');
    expect(formatPhone(undefined)).toBe('');
    expect(formatPhone('')).toBe('');
  });
});
