import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

const SUCCESS_DURATION_MS = 5000;
const ERROR_DURATION_MS = 8000;

/**
 * Transient feedback through `mat-snack-bar` (announced by the CDK live announcer).
 * Use for confirmations after an action completes; keep errors that block a form inline.
 */
@Injectable({ providedIn: 'root' })
export class Notifier {
  private readonly snackBar = inject(MatSnackBar);

  success(message: string): void {
    this.snackBar.open(message, 'Dismiss', { duration: SUCCESS_DURATION_MS, politeness: 'polite' });
  }

  error(message: string): void {
    this.snackBar.open(message, 'Dismiss', { duration: ERROR_DURATION_MS, politeness: 'assertive' });
  }
}
