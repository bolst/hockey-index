import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { askReason } from './reason-dialog';

describe('reason dialog', () => {
  const tick = async () => {
    TestBed.tick();
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
  };

  function button(name: string): HTMLButtonElement {
    const match = Array.from(document.querySelectorAll<HTMLButtonElement>('mat-dialog-container button')).find(
      (b) => b.textContent?.trim() === name,
    );
    if (!match) throw new Error(`No dialog button "${name}"`);
    return match;
  }

  it('requires a reason and emits it trimmed', async () => {
    const reasons: string[] = [];
    askReason(TestBed.inject(MatDialog), { title: 'Ban host', confirmLabel: 'Ban' }).subscribe((r) => reasons.push(r));
    await tick();

    button('Ban').click();
    await tick();
    expect(document.querySelector('mat-error')?.textContent).toContain('Enter a reason.');
    expect(reasons).toEqual([]);

    const textarea = document.querySelector<HTMLTextAreaElement>('textarea')!;
    expect(document.querySelector(`label[for="${textarea.id}"]`)?.textContent).toContain('Reason');
    textarea.value = '  spam listings  ';
    textarea.dispatchEvent(new Event('input'));
    button('Ban').click();
    await vi.waitFor(() => expect(reasons).toEqual(['spam listings']));
  });

  it('emits nothing when cancelled', async () => {
    const reasons: string[] = [];
    askReason(TestBed.inject(MatDialog), { title: 'Ban host', confirmLabel: 'Ban' }).subscribe((r) => reasons.push(r));
    await tick();
    button('Cancel').click();
    await tick();
    expect(reasons).toEqual([]);
  });
});
