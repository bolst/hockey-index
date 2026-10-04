import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SkillLevelsLink } from './skill-levels-link';

describe('SkillLevelsLink', () => {
  async function render(newTab: boolean): Promise<HTMLAnchorElement> {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(SkillLevelsLink);
    fixture.componentRef.setInput('newTab', newTab);
    await fixture.whenStable();
    return (fixture.nativeElement as HTMLElement).querySelector('a')!;
  }

  it('links to the guide in the same tab by default', async () => {
    const link = await render(false);
    expect(link.getAttribute('href')).toBe('/skill-levels');
    expect(link.textContent?.trim()).toBe('What do levels mean?');
    expect(link.hasAttribute('target')).toBe(false);
  });

  it('opens in a new tab and says so', async () => {
    const link = await render(true);
    expect(link.getAttribute('href')).toBe('/skill-levels');
    expect(link.getAttribute('target')).toBe('_blank');
    expect(link.getAttribute('rel')).toBe('noopener');
    expect(link.textContent).toContain('(opens in a new tab)');
  });
});
