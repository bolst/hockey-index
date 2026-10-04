import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { confirm } from './confirm-dialog';

describe('confirm dialog', () => {
  function button(name: string): HTMLButtonElement {
    const match = Array.from(document.querySelectorAll<HTMLButtonElement>('mat-dialog-container button')).find(
      (b) => b.textContent?.trim() === name,
    );
    if (!match) throw new Error(`No dialog button "${name}"`);
    return match;
  }

  async function open(): Promise<{ result: Promise<boolean> }> {
    const result = firstValueFrom(
      confirm(TestBed.inject(MatDialog), {
        title: 'Delete draft?',
        message: 'This cannot be undone.',
        confirmLabel: 'Delete',
        destructive: true,
      }),
    );
    TestBed.tick();
    await new Promise((resolve) => setTimeout(resolve));
    return { result };
  }

  it('shows the title as the dialog heading and resolves true on confirm', async () => {
    const { result } = await open();
    expect(document.querySelector('[role="dialog"] h2')?.textContent).toContain('Delete draft?');
    button('Delete').click();
    expect(await result).toBe(true);
  });

  it('resolves false on cancel', async () => {
    const { result } = await open();
    button('Cancel').click();
    expect(await result).toBe(false);
  });
});
