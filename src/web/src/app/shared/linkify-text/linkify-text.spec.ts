import { TestBed } from '@angular/core/testing';
import { LinkifyText } from './linkify-text';
import { MAX_URLS, normalizeUrl, segmentText } from './url-extractor';

function linkUrls(text: string): string[] {
  return segmentText(text).flatMap((segment) => (segment.kind === 'link' ? [segment.url] : []));
}

describe('segmentText (mirrors server UrlExtractorTests)', () => {
  it.each([
    ['Sign up at https://example.com/signup.', 'https://example.com/signup'],
    ['Sign up (https://example.com/signup), thanks', 'https://example.com/signup'],
    [
      'See https://en.wikipedia.org/wiki/Hockey_(disambiguation) today',
      'https://en.wikipedia.org/wiki/Hockey_(disambiguation)',
    ],
    ['Really? https://example.com/a?b=1!', 'https://example.com/a?b=1'],
    ['<https://example.com/x>', 'https://example.com/x'],
    ['"https://example.com/q"', 'https://example.com/q'],
    ['HTTPS://EXAMPLE.COM/Path', 'https://example.com/Path'],
    ['Http://Example.com:80/x', 'http://example.com/x'],
    ['https://example.com:443/x#section', 'https://example.com/x'],
    ['https://example.com./x', 'https://example.com/x'],
  ])('extracts and canonicalizes %s', (text, expected) => {
    expect(linkUrls(text)).toEqual([expected]);
  });

  it('keeps the text around the link intact, including trimmed punctuation', () => {
    expect(segmentText('Sign up (https://example.com/signup), thanks')).toEqual([
      { kind: 'text', text: 'Sign up (' },
      { kind: 'link', text: 'https://example.com/signup', url: 'https://example.com/signup' },
      { kind: 'text', text: '), thanks' },
    ]);
  });

  it('converts IDN hosts to punycode but shows the original text', () => {
    expect(segmentText('Info: https://bücher.example/kurs')).toEqual([
      { kind: 'text', text: 'Info: ' },
      { kind: 'link', text: 'https://bücher.example/kurs', url: 'https://xn--bcher-kva.example/kurs' },
    ]);
  });

  it.each([
    'javascript:alert(1)',
    'data:text/html;base64,PHNjcmlwdD4=',
    'mailto:host@example.com',
    'ftp://example.com/file',
    'www.example.com without scheme',
    'xhttp://example.com',
    'ahttps://example.com',
    'é https://ok.example but éhttps://bad.example',
    '',
  ])('does not link non-http text: %s', (text) => {
    const links = linkUrls(text);
    expect(links.every((url) => url === 'https://ok.example/')).toBe(true);
  });

  it.each([
    ['https://user:pass@example.com/', 'UserInfo'],
    ['https://google.com@evil.example/', 'UserInfo'],
    ['https://@evil.example/', 'UserInfo'],
    ['https://example.com:8443/', 'NonDefaultPort'],
    ['http://example.com:443/', 'NonDefaultPort'],
    ['https:///nohost', 'InvalidUrl'],
  ])('rejects %s as %s and leaves it as text', (text, reason) => {
    expect(normalizeUrl(text)).toEqual({ ok: false, reason });
    expect(segmentText(text)).toEqual([{ kind: 'text', text }]);
  });

  it('links each occurrence of the same canonical URL', () => {
    expect(linkUrls('https://example.com/a HTTPS://EXAMPLE.COM/a#top https://example.com/b')).toEqual([
      'https://example.com/a',
      'https://example.com/a',
      'https://example.com/b',
    ]);
  });

  it(`links at most ${MAX_URLS} distinct URLs`, () => {
    const eleven = Array.from({ length: 11 }, (_, i) => `https://example.com/${i + 1}`).join(' ');
    const links = linkUrls(eleven);
    expect(links).toHaveLength(MAX_URLS);
    expect(links.at(-1)).toBe('https://example.com/10');
    expect(segmentText(eleven).at(-1)).toEqual({ kind: 'text', text: ' https://example.com/11' });
  });

  it('normalizes decimal and octal IP hosts like the server', () => {
    expect(normalizeUrl('http://2130706433/')).toEqual({ ok: true, url: 'http://127.0.0.1/' });
    expect(normalizeUrl('http://0177.0.0.1/')).toEqual({ ok: true, url: 'http://127.0.0.1/' });
  });
});

describe('LinkifyText', () => {
  function render(text: string, eventId = 'abcdefghij'): HTMLElement {
    const fixture = TestBed.createComponent(LinkifyText);
    fixture.componentRef.setInput('text', text);
    fixture.componentRef.setInput('eventId', eventId);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('links through /out with the canonical URL, event id, and safe rel', () => {
    const element = render('Pay at https://Example.com/pay?x=1&y=2.');
    const link = element.querySelector('a')!;
    expect(link.getAttribute('href')).toBe(
      `/out?u=${encodeURIComponent('https://example.com/pay?x=1&y=2')}&e=abcdefghij`,
    );
    expect(link.getAttribute('rel')).toBe('nofollow ugc noopener noreferrer');
    expect(link.getAttribute('target')).toBe('_blank');
    expect(link.textContent).toBe('https://Example.com/pay?x=1&y=2');
    expect(element.textContent).toBe('Pay at https://Example.com/pay?x=1&y=2.');
  });

  it('renders markup as text', () => {
    const element = render('<script>alert(1)</script> <b>bold</b> <a href="javascript:alert(1)">x</a>');
    expect(element.querySelector('script, b, a')).toBeNull();
    expect(element.textContent).toBe('<script>alert(1)</script> <b>bold</b> <a href="javascript:alert(1)">x</a>');
  });

  it('keeps line breaks in the text', () => {
    const element = render('Line one\nLine two https://example.com');
    expect(element.textContent).toBe('Line one\nLine two https://example.com');
    expect(element.querySelectorAll('a')).toHaveLength(1);
  });

  it('encodes the event id', () => {
    const element = render('https://example.com', 'a&b=c');
    expect(element.querySelector('a')!.getAttribute('href')).toContain('&e=a%26b%3Dc');
  });
});
