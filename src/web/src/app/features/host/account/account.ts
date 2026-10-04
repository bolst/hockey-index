import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatCard, MatCardActions, MatCardContent, MatCardHeader, MatCardTitle } from '@angular/material/card';
import { MatDivider } from '@angular/material/divider';
import { MatError, MatFormField, MatHint, MatLabel, MatPrefix } from '@angular/material/form-field';
import { MatIcon } from '@angular/material/icon';
import { MatInput } from '@angular/material/input';
import { MatList, MatListItem, MatListItemIcon, MatListItemLine, MatListItemMeta, MatListItemTitle } from '@angular/material/list';
import { MatProgressBar } from '@angular/material/progress-bar';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toApiProblem } from '../../../core/api/api-problem';
import { AuthApi } from '../../../core/api/auth-api';
import { Session } from '../../../core/auth/session';
import { InlineMessage } from '../../../shared/inline-message/inline-message';
import { Notifier } from '../../../shared/notifier/notifier';
import { PageHeader } from '../../../shared/page-header/page-header';
import { StatusChip } from '../../../shared/status-chip/status-chip';
import { PhonePipe } from '../../../core/format/phone.pipe';

@Component({
  selector: 'app-account',
  imports: [
    ReactiveFormsModule,
    MatButton,
    MatCard,
    MatCardActions,
    MatCardContent,
    MatCardHeader,
    MatCardTitle,
    MatDivider,
    MatError,
    MatFormField,
    MatHint,
    MatIcon,
    MatInput,
    MatLabel,
    MatList,
    MatListItem,
    MatListItemIcon,
    MatListItemLine,
    MatListItemMeta,
    MatListItemTitle,
    MatPrefix,
    MatProgressBar,
    InlineMessage,
    PageHeader,
    StatusChip,
    PhonePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'app-page' },
  templateUrl: './account.html',
  styleUrl: './account.scss',
})
export class Account {
  private readonly authApi = inject(AuthApi);
  private readonly router = inject(Router);
  private readonly notifier = inject(Notifier);
  protected readonly session = inject(Session);

  protected readonly newEmail = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.email],
  });
  protected readonly code = new FormControl('', { nonNullable: true, validators: [Validators.required] });
  protected readonly awaitingCode = signal(false);
  protected readonly pendingEmail = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected async addEmail(): Promise<void> {
    if (this.newEmail.invalid) {
      this.newEmail.markAsTouched();
      return;
    }
    const email = this.newEmail.value.trim();
    await this.run(async () => {
      await firstValueFrom(this.authApi.addEmail(email));
      this.pendingEmail.set(email);
      this.awaitingCode.set(true);
      this.notifier.success(`We emailed a code and a confirmation link to ${email}.`);
    });
  }

  protected async confirmCode(): Promise<void> {
    if (this.code.invalid) {
      this.code.markAsTouched();
      return;
    }
    await this.run(async () => {
      this.session.signedIn(await firstValueFrom(this.authApi.confirmEmail({ code: this.code.value.trim() })));
      this.awaitingCode.set(false);
      this.newEmail.reset();
      this.code.reset();
      this.notifier.success('Your email is confirmed.');
    });
  }

  protected useDifferentEmail(): void {
    this.awaitingCode.set(false);
    this.code.reset();
    this.error.set(null);
  }

  protected async removeEmail(): Promise<void> {
    await this.run(async () => {
      this.session.signedIn(await firstValueFrom(this.authApi.removeEmail()));
      this.notifier.success('Email removed. You can sign in with your phone.');
    });
  }

  protected async signOut(): Promise<void> {
    await this.run(async () => {
      await this.session.signOut();
      await this.router.navigateByUrl('/');
      this.notifier.success('You are signed out.');
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
    } catch (error) {
      this.error.set(toApiProblem(error).title);
    } finally {
      this.busy.set(false);
    }
  }
}
