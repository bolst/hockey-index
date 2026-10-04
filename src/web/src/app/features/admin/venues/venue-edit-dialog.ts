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
import { AdminVenue, VenueEdit } from '../../../core/api/admin-api';
import { REASON_MAX_LENGTH } from '../../../shared/reason-dialog/reason-dialog';

export interface VenueEditResult {
  edit: VenueEdit;
  reason: string;
}

const required = [Validators.required, Validators.pattern(/\S/)];

@Component({
  selector: 'app-venue-edit-dialog',
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
    <h2 mat-dialog-title>Edit venue</h2>
    <form [formGroup]="form" (submit)="$event.preventDefault(); submit()" novalidate>
      <mat-dialog-content class="app-form">
        <mat-form-field appearance="outline">
          <mat-label>Name</mat-label>
          <input matInput formControlName="name" />
          <mat-error>Enter a name.</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Address</mat-label>
          <input matInput formControlName="addressLine" />
          <mat-error>Enter an address.</mat-error>
        </mat-form-field>
        <div class="row">
          <mat-form-field appearance="outline">
            <mat-label>City</mat-label>
            <input matInput formControlName="city" />
            <mat-error>Enter a city.</mat-error>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Region</mat-label>
            <input matInput formControlName="region" />
            <mat-error>Enter a region.</mat-error>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Country</mat-label>
            <input matInput formControlName="country" />
            <mat-error>Enter a country.</mat-error>
          </mat-form-field>
        </div>
        <div class="row">
          <mat-form-field appearance="outline">
            <mat-label>Latitude</mat-label>
            <input matInput type="number" step="any" formControlName="latitude" />
            <mat-error>Enter -90 to 90.</mat-error>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Longitude</mat-label>
            <input matInput type="number" step="any" formControlName="longitude" />
            <mat-error>Enter -180 to 180.</mat-error>
          </mat-form-field>
        </div>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Time zone</mat-label>
          <input matInput formControlName="timeZone" spellcheck="false" />
          <mat-hint>IANA name, for example America/Toronto.</mat-hint>
          <mat-error>Enter a time zone.</mat-error>
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
        <button matButton="filled" type="submit">Save</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .row {
      display: flex;
      flex-wrap: wrap;
      gap: 0 16px;
    }
    .row mat-form-field {
      flex: 1 1 140px;
    }
  `,
})
export class VenueEditDialog {
  private readonly venue = inject<AdminVenue>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject<MatDialogRef<VenueEditDialog, VenueEditResult>>(MatDialogRef);
  protected readonly reasonMax = REASON_MAX_LENGTH;
  protected readonly form = new FormGroup({
    name: new FormControl(this.venue.name, { nonNullable: true, validators: required }),
    addressLine: new FormControl(this.venue.addressLine, { nonNullable: true, validators: required }),
    city: new FormControl(this.venue.city, { nonNullable: true, validators: required }),
    region: new FormControl(this.venue.region, { nonNullable: true, validators: required }),
    country: new FormControl(this.venue.country, { nonNullable: true, validators: required }),
    latitude: new FormControl(this.venue.latitude, {
      nonNullable: true,
      validators: [Validators.required, Validators.min(-90), Validators.max(90)],
    }),
    longitude: new FormControl(this.venue.longitude, {
      nonNullable: true,
      validators: [Validators.required, Validators.min(-180), Validators.max(180)],
    }),
    timeZone: new FormControl(this.venue.timeZone, { nonNullable: true, validators: required }),
    reason: new FormControl('', { nonNullable: true, validators: required }),
  });

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { reason, latitude, longitude, ...text } = this.form.getRawValue();
    const edit: VenueEdit = { latitude: Number(latitude), longitude: Number(longitude) };
    for (const [key, value] of Object.entries(text) as [keyof typeof text, string][]) {
      edit[key] = value.trim();
    }
    this.dialogRef.close({ edit, reason: reason.trim() });
  }
}
