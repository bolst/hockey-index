import { TestBed } from '@angular/core/testing';

/** Retries `check` until it stops throwing. Avoids the `vi` global so this file type-checks in the app build. */
export async function waitUntil<T>(check: () => T | Promise<T>, timeoutMs = 1000): Promise<T> {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    try {
      return await check();
    } catch (error) {
      if (Date.now() > deadline) throw error;
      await new Promise((resolve) => setTimeout(resolve, 10));
    }
  }
}

/** Lets dialogs and overlays render in zoneless tests. */
export async function settle(): Promise<void> {
  TestBed.tick();
  await new Promise((resolve) => setTimeout(resolve));
  TestBed.tick();
}

export function buttonIn(root: ParentNode, name: string): HTMLButtonElement {
  const match = Array.from(root.querySelectorAll<HTMLButtonElement>('button, a[mat-button], a[matButton]')).find(
    (b) => b.textContent?.trim() === name || b.getAttribute('aria-label') === name,
  );
  if (!match) throw new Error(`No button "${name}"`);
  return match;
}

export function dialog(): HTMLElement {
  const container = document.querySelector<HTMLElement>('mat-dialog-container');
  if (!container) throw new Error('No open dialog');
  return container;
}

/** Finds a form control by its visible label text. */
export function fieldIn(root: ParentNode, label: string): HTMLInputElement | HTMLTextAreaElement {
  const labels = Array.from(root.querySelectorAll('label'));
  const labelEl = labels.find((l) => l.textContent?.replace(/[\s*]+$/, '').trim() === label);
  const id = labelEl?.getAttribute('for');
  const control = id ? document.getElementById(id) : null;
  if (!control) {
    throw new Error(`No field labelled "${label}" among [${labels.map((l) => JSON.stringify(l.textContent)).join(', ')}]`);
  }
  return control as HTMLInputElement | HTMLTextAreaElement;
}

export function typeInto(control: HTMLInputElement | HTMLTextAreaElement, value: string): void {
  control.value = value;
  control.dispatchEvent(new Event('input'));
}

export async function confirmReason(reason: string, confirmLabel: string): Promise<void> {
  const field = await waitUntil(() => fieldIn(dialog(), 'Reason'));
  typeInto(field, reason);
  await submitDialog(confirmLabel);
}

/** Clicks a dialog button and waits until the dialog has closed. */
export async function submitDialog(buttonName: string): Promise<void> {
  buttonIn(dialog(), buttonName).click();
  await waitUntil(async () => {
    await settle();
    if (document.querySelector('mat-dialog-container')) throw new Error('Dialog still open');
  });
}
