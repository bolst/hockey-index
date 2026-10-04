import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatError, MatFormField, MatHint, MatLabel } from '@angular/material/form-field';
import { MatIcon } from '@angular/material/icon';
import { MatInput } from '@angular/material/input';
import { Router } from '@angular/router';

const GUID_PATTERN = /^\s*[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\s*$/;

@Component({
  selector: 'app-admin-host-lookup',
  imports: [ReactiveFormsModule, MatButton, MatIcon, MatFormField, MatLabel, MatInput, MatHint, MatError],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'app-stack' },
  template: `
    <h2 class="section-title">Find a host</h2>
    <form class="app-form" (submit)="$event.preventDefault(); submit()" novalidate>
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>Host ID</mat-label>
        <input matInput [formControl]="hostId" autocomplete="off" spellcheck="false" />
        <mat-hint>The host's GUID, from the queue or the audit log.</mat-hint>
        <mat-error>Enter a host ID like 3f2504e0-4f89-11d3-9a0c-0305e82c3301.</mat-error>
      </mat-form-field>
      <div class="app-actions">
        <button matButton="filled" type="submit">
          <mat-icon aria-hidden="true">search</mat-icon>
          Look up
        </button>
      </div>
    </form>
  `,
  styles: `
    .section-title {
      margin: 0;
      font: var(--mat-sys-title-large);
    }
    form {
      max-width: 560px;
    }
  `,
})
export class HostLookup {
  private readonly router = inject(Router);
  protected readonly hostId = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.pattern(GUID_PATTERN)],
  });

  protected submit(): void {
    if (this.hostId.invalid) {
      this.hostId.markAsTouched();
      return;
    }
    void this.router.navigate(['/admin/hosts', this.hostId.value.trim().toLowerCase()]);
  }
}
