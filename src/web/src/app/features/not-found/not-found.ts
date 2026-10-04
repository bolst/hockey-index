import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { EmptyState } from '../../shared/empty-state/empty-state';

@Component({
  selector: 'app-not-found',
  imports: [RouterLink, MatButton, MatIcon, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'app-page' },
  template: `
    <app-empty-state icon="explore_off" headline="Page not found" [headingLevel]="1">
      <p>The link may be old, or the page moved. Check the address, or head back to search.</p>
      <ng-container emptyStateActions>
        <a matButton="filled" routerLink="/">
          <mat-icon aria-hidden="true">search</mat-icon>
          Back to search
        </a>
      </ng-container>
    </app-empty-state>
  `,
})
export class NotFound {}
