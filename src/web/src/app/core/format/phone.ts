/**
 * Display form of an E.164 number. NANP numbers (+1) read as `+1 416-555-0142`; other numbers keep
 * E.164 with a space after the country code when it can be told apart, else stay unchanged.
 */
export function formatPhone(e164: string | null | undefined): string {
  if (!e164) {
    return '';
  }
  const nanp = /^\+1(\d{3})(\d{3})(\d{4})$/.exec(e164);
  if (nanp) {
    return `+1 ${nanp[1]}-${nanp[2]}-${nanp[3]}`;
  }
  return e164;
}
