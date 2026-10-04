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
import { AdminVenue } from '../../../core/api/admin-api';
import { REASON_MAX_LENGTH } from '../../../shared/reason-dialog/reason-dialog';

export interface VenueMergeResult {
  intoId: string;
  reason: string;
}

@Component({
  selector: 'app-venue-merge-dialog',
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
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>Merge venue</h2>
    <form [formGroup]="form" (submit)="$event.preventDefault(); submit()" novalidate>
      <mat-dialog-content class="app-form">
        <p class="message">
          Events at {{ venue.name }} ({{ venue.publicId }}) move to the target venue, and this venue is removed.
        </p>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Merge into venue ID</mat-label>
          <input matInput formControlName="intoId" autocomplete="off" spellcheck="false" />
          <mat-hint>The public ID of the venue to keep.</mat-hint>
          <mat-error>Enter the venue ID to merge into.</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Reason</mat-label>
          <textarea matInput rows="2" formControlName="reason" [maxlength]="reasonMax"></textarea>
          <mat-hint>Saved to the audit log.</mat-hint>
          <mat-error>Enter a reason.</mat-error>
        </mat-form-field>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button matButton type="button" mat-dialog-close>Cancel</button>
        <button matButton="filled" type="submit" class="destructive">Merge</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .message {
      margin: 0 0 8px;
      color: var(--mat-sys-on-surface-variant);
    }
    .destructive {
      --mat-button-filled-container-color: var(--mat-sys-error);
      --mat-button-filled-label-text-color: var(--mat-sys-on-error);
    }
  `,
})
export class VenueMergeDialog {
  protected readonly venue = inject<AdminVenue>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject<MatDialogRef<VenueMergeDialog, VenueMergeResult>>(MatDialogRef);
  protected readonly reasonMax = REASON_MAX_LENGTH;
  protected readonly form = new FormGroup({
    intoId: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/\S/)] }),
    reason: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.pattern(/\S/)] }),
  });

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { intoId, reason } = this.form.getRawValue();
    this.dialogRef.close({ intoId: intoId.trim(), reason: reason.trim() });
  }
}
