import { ChangeDetectionStrategy, Component, inject, signal, viewChild } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { MatButtonToggle, MatButtonToggleGroup } from '@angular/material/button-toggle';
import { MatCard, MatCardAvatar, MatCardContent, MatCardHeader, MatCardSubtitle, MatCardTitle } from '@angular/material/card';
import { MatError, MatFormField, MatHint, MatLabel, MatPrefix } from '@angular/material/form-field';
import { MatIcon } from '@angular/material/icon';
import { MatInput } from '@angular/material/input';
import { MatProgressBar } from '@angular/material/progress-bar';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { toApiProblem } from '../../../core/api/api-problem';
import { AuthApi, Me } from '../../../core/api/auth-api';
import { SESSION_UNAVAILABLE_PARAM, safeReturnUrl } from '../../../core/auth/guards';
import { Session } from '../../../core/auth/session';
import { InlineMessage } from '../../../shared/inline-message/inline-message';
import { Turnstile } from '../../../shared/turnstile/turnstile';

type Method = 'phone' | 'email';
type Step = 'start' | 'code';

@Component({
  selector: 'app-sign-in',
  imports: [
    ReactiveFormsModule,
    MatButton,
    MatButtonToggle,
    MatButtonToggleGroup,
    MatCard,
    MatCardAvatar,
    MatCardContent,
    MatCardHeader,
    MatCardSubtitle,
    MatCardTitle,
    MatError,
    MatFormField,
    MatHint,
    MatIcon,
    MatInput,
    MatLabel,
    MatPrefix,
    MatProgressBar,
    InlineMessage,
    Turnstile,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'app-page' },
  templateUrl: './sign-in.html',
  styleUrl: './sign-in.scss',
})
export class SignIn {
  private readonly authApi = inject(AuthApi);
  private readonly session = inject(Session);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly turnstile = viewChild(Turnstile);

  protected readonly sessionUnavailable = this.route.snapshot.queryParamMap.has(SESSION_UNAVAILABLE_PARAM);
  protected readonly method = signal<Method>('phone');
  protected readonly step = signal<Step>('start');
  protected readonly phone = new FormControl('', { nonNullable: true, validators: [Validators.required] });
  protected readonly email = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.email],
  });
  protected readonly code = new FormControl('', { nonNullable: true, validators: [Validators.required] });
  protected readonly emailHint = signal<string | null>(null);
  protected readonly turnstileToken = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private loginId: string | null = null;

  protected useMethod(method: Method): void {
    this.method.set(method);
    this.step.set('start');
    this.code.reset();
    this.error.set(null);
  }

  protected async start(): Promise<void> {
    const field = this.method() === 'phone' ? this.phone : this.email;
    if (field.invalid) {
      field.markAsTouched();
      return;
    }
    const turnstileToken = this.turnstileToken();
    if (!turnstileToken) {
      this.error.set('Complete the human check first.');
      return;
    }
    await this.run(async () => {
      try {
        if (this.method() === 'phone') {
          await firstValueFrom(this.authApi.startPhone({ phone: this.phone.value.trim(), turnstileToken }));
        } else {
          const started = await firstValueFrom(
            this.authApi.startEmailLogin({ email: this.email.value.trim(), turnstileToken }),
          );
          this.loginId = started.loginId;
        }
        this.emailHint.set(null);
        this.step.set('code');
      } finally {
        this.turnstile()?.reset();
      }
    });
  }

  protected async verify(): Promise<void> {
    if (this.code.invalid) {
      this.code.markAsTouched();
      return;
    }
    await this.run(async () => {
      const code = this.code.value.trim();
      if (this.method() === 'phone' && !this.loginId) {
        const result = await firstValueFrom(this.authApi.verifyPhone({ phone: this.phone.value.trim(), code }));
        if (result.kind === 'signedIn') {
          await this.finish(result.me);
          return;
        }
        this.loginId = result.check.loginId;
        this.emailHint.set(result.check.emailHint);
        this.code.reset();
        return;
      }
      await this.finish(await firstValueFrom(this.authApi.completeEmailLogin({ loginId: this.loginId!, code })));
    });
  }

  protected restart(): void {
    this.loginId = null;
    this.emailHint.set(null);
    this.code.reset();
    this.error.set(null);
    this.step.set('start');
  }

  private async finish(me: Me): Promise<void> {
    this.session.signedIn(me);
    await this.router.navigateByUrl(safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl')));
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
