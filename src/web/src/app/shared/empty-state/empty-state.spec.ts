import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { EmptyState } from './empty-state';

@Component({
  imports: [EmptyState],
  template: `
    <app-empty-state icon="event" headline="No events yet" [headingLevel]="level()">
      <p>Events you create appear here.</p>
      <a emptyStateActions href="/host/new">Create event</a>
    </app-empty-state>
  `,
})
class Host {
  readonly level = signal<1 | 2>(2);
}

describe('EmptyState', () => {
  it('renders a labelled region with an h2, supporting text, and the action', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const region = (fixture.nativeElement as HTMLElement).querySelector('app-empty-state')!;
    expect(region.getAttribute('role')).toBe('region');
    expect(region.getAttribute('aria-label')).toBe('No events yet');
    expect(region.querySelector('h2')?.textContent).toBe('No events yet');
    expect(region.querySelector('h1')).toBeNull();
    expect(region.textContent).toContain('Events you create appear here.');
    expect(region.querySelector('a')?.getAttribute('href')).toBe('/host/new');
  });

  it('hides the decorative icon from assistive technology', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const icon = (fixture.nativeElement as HTMLElement).querySelector('mat-icon')!;
    expect(icon.closest('[aria-hidden="true"]')).not.toBeNull();
    expect(icon.textContent).toBe('event');
  });

  it('can render the headline as the page h1', async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.level.set(1);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('h1')?.textContent).toBe('No events yet');
    expect(element.querySelector('h2')).toBeNull();
  });
});
