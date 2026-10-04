import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import {
  MAT_DIALOG_DATA,
  MatDialogActions,
  MatDialogClose,
  MatDialogContent,
  MatDialogRef,
  MatDialogTitle,
} from '@angular/material/dialog';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatOption, MatSelect } from '@angular/material/select';
import { REASON_MAX_LENGTH } from '../../../shared/reason-dialog/reason-dialog';

export interface OpenBreakerData {
  label: string;
}

export interface OpenBreakerResult {
  minutes: number;
  reason: string;
}

export const BREAKER_DURATIONS = [
  { minutes: 15, label: '15 minutes' },
  { minutes: 60, label: '1 hour' },
  { minutes: 240, label: '4 hours' },
  { minutes: 1440, label: '24 hours' },
];

@Component({
  selector: 'app-open-breaker-dialog',
  imports: [
    ReactiveFormsModule,
    MatButton,
    MatDialogTitle,
    MatDialogContent,
    MatDialogActions,
    MatDialogClose,
    MatFormField,
    MatLabel,
    MatHint,
    MatError,
    MatInput,
    MatSelect,
    MatOption,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>Open breaker: {{ data.label }}</h2>
    <form [formGroup]="form" (submit)="$event.preventDefault(); submit()" novalidate>
      <mat-dialog-content class="app-form">
        <p class="message">While open, the feature is turned off for everyone.</p>
        <mat-form-field appearance="outline">
          <mat-label>Duration</mat-label>
          <mat-select formControlName="minutes">
            @for (d of durations; track d.minutes) {
              <mat-option [value]="d.minutes">{{ d.label }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Reason</mat-label>
          <textarea matInput rows="3" formControlName="reason" [maxlength]="reasonMax"></textarea>
          <mat-hint>Saved to the audit log.</mat-hint>
          <mat-error>Enter a reason.</mat-error>
        </mat-form-field>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button matButton type="button" mat-dialog-close>Cancel</button>
        <button matButton="filled" type="submit">Open breaker</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .message {
      margin: 0 0 8px;
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class OpenBreakerDialog {
  protected readonly data = inject<OpenBreakerData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject<MatDialogRef<OpenBreakerDialog, OpenBreakerResult>>(MatDialogRef);
  protected readonly durations = BREAKER_DURATIONS;
  protected readonly reasonMax = REASON_MAX_LENGTH;
  protected readonly form = new FormGroup({
    minutes: new FormControl(60, { nonNullable: true }),
    reason: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/\S/)] }),
  });

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { minutes, reason } = this.form.getRawValue();
    this.dialogRef.close({ minutes, reason: reason.trim() });
  }
}
