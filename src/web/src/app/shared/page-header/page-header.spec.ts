import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { PageHeader } from './page-header';

@Component({
  imports: [PageHeader],
  template: `
    <app-page-header headline="Your events" supportingText="Games you host.">
      <button pageHeaderActions type="button">New event</button>
    </app-page-header>
  `,
})
class Host {}

describe('PageHeader', () => {
  it('renders one h1, supporting text, and projected actions', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelectorAll('h1')).toHaveLength(1);
    expect(element.querySelector('h1')?.textContent).toBe('Your events');
    expect(element.textContent).toContain('Games you host.');
    expect(element.querySelector('button')?.textContent).toBe('New event');
  });

  it('omits the supporting paragraph when no text is given', async () => {
    const fixture = TestBed.createComponent(PageHeader);
    fixture.componentRef.setInput('headline', 'Account');
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('p')).toBeNull();
  });
});
