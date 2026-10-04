import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogActions, MatDialogClose, MatDialogContent, MatDialogTitle } from '@angular/material/dialog';
import { AuditEntry } from '../../../core/api/admin-api';

function prettyJson(text: string | null): string {
  if (!text) {
    return 'No metadata.';
  }
  try {
    return JSON.stringify(JSON.parse(text), null, 2);
  } catch {
    return text;
  }
}

@Component({
  selector: 'app-audit-details-dialog',
  imports: [MatButton, MatDialogTitle, MatDialogContent, MatDialogActions, MatDialogClose],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ entry.action }}</h2>
    <mat-dialog-content>
      <p class="target">{{ entry.targetType }} {{ entry.targetId }}</p>
      <pre data-testid="audit-metadata">{{ metadata }}</pre>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton type="button" mat-dialog-close cdkFocusInitial>Close</button>
    </mat-dialog-actions>
  `,
  styles: `
    .target {
      margin: 0 0 8px;
      overflow-wrap: anywhere;
      color: var(--mat-sys-on-surface-variant);
    }
    pre {
      margin: 0;
      padding: 12px;
      overflow-x: auto;
      border-radius: var(--mat-sys-corner-small);
      background: var(--mat-sys-surface-container-high);
      font: var(--mat-sys-body-small);
      font-family: monospace;
    }
  `,
})
export class AuditDetailsDialog {
  protected readonly entry = inject<AuditEntry>(MAT_DIALOG_DATA);
  protected readonly metadata = prettyJson(this.entry.metadata);
}
