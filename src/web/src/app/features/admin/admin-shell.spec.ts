import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { AdminShell } from './admin-shell';

describe('AdminShell', () => {
  it('renders one h1 and a tab link per section', async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: 'admin', component: AdminShell, children: [] }])],
    });
    const harness = await RouterTestingHarness.create('/admin');
    const element = harness.routeNativeElement as HTMLElement;
    expect(element.querySelectorAll('h1')).toHaveLength(1);
    expect(element.querySelector('h1')?.textContent).toBe('Admin');
    const nav = element.querySelector('nav[aria-label="Admin sections"]')!;
    const labels = Array.from(nav.querySelectorAll('a')).map((a) => a.textContent?.trim());
    expect(labels).toEqual(['Queue', 'Hosts', 'Venues', 'Blocklist', 'Invites', 'Breakers', 'Audit']);
  });
});
