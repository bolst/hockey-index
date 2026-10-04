import { ChangeDetectionStrategy, Component, inject, signal, viewChild } from '@angular/core';
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
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatOption, MatSelect } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import { toApiProblem } from '../../core/api/api-problem';
import { DiscoveryApi, ReportReason } from '../../core/api/discovery-api';
import { InlineMessage } from '../../shared/inline-message/inline-message';
import { Notifier } from '../../shared/notifier/notifier';
import { Turnstile } from '../../shared/turnstile/turnstile';

export interface ReportDialogData {
  publicId: string;
  title: string;
}

export const REPORT_DETAILS_MAX_LENGTH = 500;

export const REPORT_REASONS: readonly { value: ReportReason; label: string }[] = [
  { value: 'scam', label: 'Scam or asks for money up front' },
  { value: 'spam', label: 'Spam or advertising' },
  { value: 'offensive', label: 'Offensive or abusive' },
  { value: 'inaccurate', label: 'Wrong time, place or details' },
  { value: 'other', label: 'Something else' },
];

function problemMessage(code: string | null, status: number): string {
  switch (code) {
    case 'turnstile_failed':
      return 'The human check failed. Complete it again and resend.';
    case 'report_limit_reached':
      return 'You have sent several reports in a short time. Try again later.';
    case 'invalid_report':
      return 'Check the reason and details, then try again.';
    default:
      return status === 404 ? 'This event is no longer listed.' : 'Your report could not be sent. Try again.';
  }
}

@Component({
  selector: 'app-report-dialog',
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
    MatProgressBar,
    InlineMessage,
    Turnstile,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>Report this event</h2>
    <form (submit)="$event.preventDefault(); submit()" novalidate>
      <mat-dialog-content class="content">
        <p class="intro">
          Tell us what is wrong with <strong>{{ data.title }}</strong>. Reports are anonymous; the host does not see who sent them.
        </p>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Reason</mat-label>
          <mat-select [formControl]="reason">
            @for (option of reasons; track option.value) {
              <mat-option [value]="option.value">{{ option.label }}</mat-option>
            }
          </mat-select>
          <mat-error>Choose a reason.</mat-error>
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Details (optional)</mat-label>
          <textarea matInput rows="4" [maxlength]="maxLength" [formControl]="details"></textarea>
          <mat-hint>What should a moderator look at?</mat-hint>
          <mat-hint align="end">{{ details.value.length }}/{{ maxLength }}</mat-hint>
        </mat-form-field>
        <app-turnstile action="report" (tokenChange)="token.set($event)" />
        @if (error(); as message) {
          <app-inline-message kind="error">{{ message }}</app-inline-message>
        }
      </mat-dialog-content>
      @if (busy()) {
        <mat-progress-bar mode="indeterminate" aria-label="Sending report" />
      }
      <mat-dialog-actions align="end">
        <button matButton type="button" mat-dialog-close>Cancel</button>
        <button matButton="filled" type="submit" [disabled]="busy() || !token()">Send report</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .content {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .intro {
      margin: 0;
      color: var(--mat-sys-on-surface-variant);
    }

    mat-form-field {
      width: 100%;
    }
  `,
})
export class ReportDialog {
  protected readonly data = inject<ReportDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject<MatDialogRef<ReportDialog, boolean>>(MatDialogRef);
  private readonly api = inject(DiscoveryApi);
  private readonly notifier = inject(Notifier);
  private readonly turnstile = viewChild.required(Turnstile);

  protected readonly reasons = REPORT_REASONS;
  protected readonly maxLength = REPORT_DETAILS_MAX_LENGTH;
  protected readonly reason = new FormControl<ReportReason | null>(null, { validators: [Validators.required] });
  protected readonly details = new FormControl('', { nonNullable: true });
  protected readonly token = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected async submit(): Promise<void> {
    const token = this.token();
    if (this.reason.invalid || !this.reason.value) {
      this.reason.markAsTouched();
      return;
    }
    if (!token) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      const details = this.details.value.trim();
      await firstValueFrom(
        this.api.report(this.data.publicId, {
          reason: this.reason.value,
          ...(details ? { details } : {}),
          turnstileToken: token,
        }),
      );
      this.notifier.success('Thanks. A moderator will review this event.');
      this.dialogRef.close(true);
    } catch (error) {
      const problem = toApiProblem(error);
      this.error.set(problemMessage(problem.code, problem.status));
      this.turnstile().reset();
    } finally {
      this.busy.set(false);
    }
  }
}

export function openReportDialog(dialog: MatDialog, data: ReportDialogData): MatDialogRef<ReportDialog, boolean> {
  return dialog.open<ReportDialog, ReportDialogData, boolean>(ReportDialog, {
    data,
    width: '520px',
    maxWidth: 'calc(100vw - 32px)',
    autoFocus: 'first-tabbable',
  });
}
