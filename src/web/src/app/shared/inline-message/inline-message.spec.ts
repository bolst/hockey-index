import { TestBed } from '@angular/core/testing';
import { InlineMessage, InlineMessageKind } from './inline-message';

function render(kind: InlineMessageKind) {
  const fixture = TestBed.createComponent(InlineMessage);
  fixture.componentRef.setInput('kind', kind);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('InlineMessage', () => {
  it('announces errors with role="alert"', () => {
    expect(render('error').getAttribute('role')).toBe('alert');
  });

  it.each(['info', 'success'] as const)('uses role="status" for %s', (kind) => {
    expect(render(kind).getAttribute('role')).toBe('status');
  });

  it('shows a decorative icon matching the kind', () => {
    const icon = render('success').querySelector('mat-icon')!;
    expect(icon.getAttribute('aria-hidden')).toBe('true');
    expect(icon.textContent).toBe('check_circle');
  });
});
