import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SkillLevels } from './skill-levels';

describe('SkillLevels', () => {
  async function render(): Promise<HTMLElement> {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(SkillLevels);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders the ten levels in order under one h1', async () => {
    const root = await render();
    expect(root.querySelectorAll('h1')).toHaveLength(1);
    expect(root.querySelector('h1')?.textContent).toBe('Skill levels');
    const titles = Array.from(root.querySelectorAll('ol > li h2')).map((heading) => heading.textContent?.trim());
    expect(titles).toEqual(['0', '1', '2', '2.5', '3', '3.5', '4', '5', '6', '7'].map((level) => `Level ${level}`));
  });

  it('shows equivalent leagues for levels 5, 6 and 7 only', async () => {
    const root = await render();
    const levels = Array.from(root.querySelectorAll<HTMLElement>('[data-testid="skill-level"]'));
    const withLeagues = levels
      .filter((level) => level.querySelector('[data-testid="skill-leagues"]'))
      .map((level) => level.querySelector('h2')?.textContent?.trim());
    expect(withLeagues).toEqual(['Level 5', 'Level 6', 'Level 7']);
    const leagues = (level: HTMLElement) => Array.from(level.querySelectorAll('.league')).map((item) => item.textContent?.trim());
    expect(leagues(levels[7])).toEqual(['NCAA D2/D3', 'CJHL', 'USHL', 'NAHL']);
    expect(leagues(levels[8])).toEqual(['NCAA D1', 'CHL']);
    expect(leagues(levels[9])).toEqual(['NHL', 'AHL']);
  });

  it('explains half levels and links back to search', async () => {
    const root = await render();
    const note = root.querySelector('aside')!;
    expect(note.querySelector('h2')?.textContent).toBe('Half levels');
    expect(note.textContent).toContain('includes 2 or 3');
    expect(note.textContent).toContain('3.5');
    const find = Array.from(root.querySelectorAll('a')).find((link) => link.textContent?.includes('Find events'));
    expect(find?.getAttribute('href')).toBe('/');
  });
});
