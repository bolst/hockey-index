import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButton } from '@angular/material/button';
import { MatCard, MatCardActions, MatCardAvatar, MatCardContent, MatCardHeader, MatCardTitle } from '@angular/material/card';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toApiProblem } from '../../../core/api/api-problem';
import { AuthApi } from '../../../core/api/auth-api';
import { takeFragmentToken } from '../../../core/auth/fragment-token';
import { SIGN_IN_PATH } from '../../../core/auth/guards';
import { Session } from '../../../core/auth/session';
import { InlineMessage } from '../../../shared/inline-message/inline-message';

export type EmailLinkPurpose = 'confirm' | 'login';

/**
 * Landing page for emailed links (`/auth/email/confirm#t=…`, `/auth/email/login#t=…`).
 * The token is moved into memory and the fragment cleared on load; it is POSTed only after a click,
 * so mail scanners that open links cannot redeem it (plan R15).
 */
@Component({
  selector: 'app-email-link',
  imports: [
    RouterLink,
    MatButton,
    MatCard,
    MatCardActions,
    MatCardAvatar,
    MatCardContent,
    MatCardHeader,
    MatCardTitle,
    MatIcon,
    MatProgressBar,
    InlineMessage,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'app-page' },
  template: `
    <mat-card appearance="outlined" class="link-card">
      <div class="progress-slot">
        @if (busy()) {
          <mat-progress-bar mode="indeterminate" aria-label="Working" />
        }
      </div>
      <mat-card-header>
        <div mat-card-avatar class="avatar" aria-hidden="true">
          <mat-icon>{{ done() ? 'mark_email_read' : purpose === 'confirm' ? 'mail' : 'login' }}</mat-icon>
        </div>
        <h1 mat-card-title>{{ purpose === 'confirm' ? 'Confirm your email' : 'Sign in with email' }}</h1>
      </mat-card-header>
      <mat-card-content class="app-stack">
        @if (done()) {
          <p role="status">Your email is confirmed.</p>
        } @else if (hasToken()) {
          <p>
            {{ purpose === 'confirm' ? 'Select the button to confirm this email for your host account.' : 'Select the button to finish signing in.' }}
          </p>
        } @else {
          <p>This link is used or incomplete. Open the newest link from your email, or enter the code instead.</p>
        }
        @if (error(); as message) {
          <app-inline-message kind="error">{{ message }}</app-inline-message>
        }
      </mat-card-content>
      <mat-card-actions align="end">
        @if (done()) {
          <a matButton="filled" routerLink="/host/account">Go to your account</a>
        } @else if (hasToken()) {
          <button matButton="filled" type="button" [disabled]="busy()" (click)="submit()">
            {{ purpose === 'confirm' ? 'Confirm email' : 'Sign in' }}
          </button>
        } @else {
          <a matButton="outlined" [routerLink]="purpose === 'confirm' ? '/host/account' : signInPath">
            {{ purpose === 'confirm' ? 'Enter the code on your account page' : 'Go to sign in' }}
          </a>
        }
      </mat-card-actions>
    </mat-card>
  `,
  styles: `
    :host {
      max-width: 520px;
      padding-top: 16px;
    }

    .link-card {
      position: relative;
      overflow: hidden;
      padding: 8px;
    }

    .progress-slot {
      position: absolute;
      inset: 0 0 auto;
      height: 4px;
    }

    .avatar {
      display: grid;
      place-items: center;
      background: var(--mat-sys-primary-container);
      color: var(--mat-sys-on-primary-container);
    }

    h1[mat-card-title] {
      margin: 0;
      font: var(--mat-sys-headline-small);
    }

    mat-card-header {
      margin-bottom: 16px;
    }

    p {
      margin: 0;
      color: var(--mat-sys-on-surface-variant);
    }

    mat-card-actions {
      padding: 8px 16px 8px;
    }
  `,
})
export class EmailLink {
  private readonly authApi = inject(AuthApi);
  private readonly session = inject(Session);
  private readonly router = inject(Router);

  protected readonly purpose: EmailLinkPurpose = inject(ActivatedRoute).snapshot.data['purpose'] ?? 'confirm';
  protected readonly signInPath = SIGN_IN_PATH;
  private token = takeFragmentToken(inject(DOCUMENT).defaultView!);
  protected readonly hasToken = signal(this.token !== null);
  protected readonly busy = signal(false);
  protected readonly done = signal(false);
  protected readonly error = signal<string | null>(null);

  protected async submit(): Promise<void> {
    const token = this.token;
    if (!token) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      if (this.purpose === 'confirm') {
        this.session.signedIn(await firstValueFrom(this.authApi.confirmEmail({ token })));
        this.done.set(true);
      } else {
        this.session.signedIn(await firstValueFrom(this.authApi.completeEmailLogin({ token })));
        await this.router.navigateByUrl('/host');
      }
      this.forgetToken();
    } catch (error) {
      const problem = toApiProblem(error);
      if (problem.status === 401) {
        this.error.set('Sign in to your host account first, then open the link from your email again.');
      } else {
        this.error.set(problem.title);
      }
      if (problem.status !== 0) {
        this.forgetToken();
      }
    } finally {
      this.busy.set(false);
    }
  }

  private forgetToken(): void {
    this.token = null;
    this.hasToken.set(false);
  }
}
