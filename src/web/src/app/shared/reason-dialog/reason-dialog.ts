import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import {
  MAT_DIALOG_DATA,
  MatDialog,
  MatDialogActions,
  MatDialogClose,
  MatDialogContent,
  MatDialogRef,
  MatDialogTitle,
} from '@angular/material/dialog';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { Observable, filter } from 'rxjs';

export const REASON_MAX_LENGTH = 500;

export interface ReasonDialogData {
  title: string;
  message?: string;
  confirmLabel: string;
  destructive?: boolean;
}

/**
 * Asks for the audit reason every admin action requires. Closes with the trimmed reason, or
 * `undefined` when cancelled.
 */
@Component({
  selector: 'app-reason-dialog',
  imports: [
    ReactiveFormsModule,
    MatButton,
    MatDialogTitle,
    MatDialogContent,
    MatDialogActions,
    MatDialogClose,
    MatFormField,
    MatLabel,
    MatInput,
    MatHint,
    MatError,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <form (submit)="$event.preventDefault(); submit()" novalidate>
      <mat-dialog-content class="content">
        @if (data.message) {
          <p class="message">{{ data.message }}</p>
        }
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Reason</mat-label>
          <textarea
            matInput
            cdkFocusInitial
            rows="3"
            [maxlength]="maxLength"
            [formControl]="reason"
          ></textarea>
          <mat-hint>Saved to the audit log.</mat-hint>
          <mat-hint align="end">{{ reason.value.length }}/{{ maxLength }}</mat-hint>
          <mat-error>Enter a reason.</mat-error>
        </mat-form-field>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button matButton type="button" mat-dialog-close>Cancel</button>
        <button matButton="filled" type="submit" [class.destructive]="data.destructive">
          {{ data.confirmLabel }}
        </button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .content {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .message {
      margin: 0;
      color: var(--mat-sys-on-surface-variant);
    }

    mat-form-field {
      width: 100%;
    }

    .destructive {
      --mat-button-filled-container-color: var(--mat-sys-error);
      --mat-button-filled-label-text-color: var(--mat-sys-on-error);
    }
  `,
})
export class ReasonDialog {
  protected readonly data = inject<ReasonDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject<MatDialogRef<ReasonDialog, string>>(MatDialogRef);
  protected readonly maxLength = REASON_MAX_LENGTH;
  protected readonly reason = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.pattern(/\S/)],
  });

  protected submit(): void {
    if (this.reason.invalid) {
      this.reason.markAsTouched();
      return;
    }
    this.dialogRef.close(this.reason.value.trim());
  }
}

/** Emits the reason once, and only when the person confirms. */
export function askReason(dialog: MatDialog, data: ReasonDialogData): Observable<string> {
  return dialog
    .open<ReasonDialog, ReasonDialogData, string>(ReasonDialog, { data, width: '480px', maxWidth: 'calc(100vw - 32px)' })
    .afterClosed()
    .pipe(filter((reason): reason is string => !!reason));
}
