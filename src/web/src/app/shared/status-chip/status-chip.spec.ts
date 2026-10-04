import { TestBed } from '@angular/core/testing';
import { StatusChip } from './status-chip';

describe('StatusChip', () => {
  function render(inputs: Record<string, string>) {
    const fixture = TestBed.createComponent(StatusChip);
    Object.entries(inputs).forEach(([name, value]) => fixture.componentRef.setInput(name, value));
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders the label inside a mat-chip with the tone class on the host', () => {
    const element = render({ label: 'Cancelled', tone: 'critical' });
    expect(element.querySelector('mat-chip')?.textContent?.trim()).toBe('Cancelled');
    expect(element.classList).toContain('tone-critical');
  });

  it('adds a decorative icon only when one is given', () => {
    expect(render({ label: 'Draft' }).querySelector('mat-icon')).toBeNull();
    const icon = render({ label: 'Confirmed', tone: 'positive', icon: 'verified' }).querySelector('mat-icon')!;
    expect(icon.getAttribute('aria-hidden')).toBe('true');
  });
});
