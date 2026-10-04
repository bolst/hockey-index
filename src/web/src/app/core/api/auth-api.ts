import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface Me {
  id: string;
  phone: string;
  email: string | null;
  isVoip: boolean;
  isAdmin: boolean;
  createdAt: string;
}

export interface StartPhoneOtpRequest {
  phone: string;
  turnstileToken: string;
}

export interface VerifyPhoneOtpRequest {
  phone: string;
  code: string;
}

export interface StartEmailLoginRequest {
  email: string;
  turnstileToken: string;
}

export type CompleteEmailLoginRequest = { loginId: string; code: string } | { token: string };

export type ConfirmEmailRequest = { code: string } | { token: string };

export interface EmailLoginStarted {
  loginId: string;
}

export interface EmailCheckRequired {
  loginId: string;
  emailHint: string;
}

export type PhoneVerifyResult =
  | { kind: 'signedIn'; me: Me }
  | { kind: 'emailCheckRequired'; check: EmailCheckRequired };

@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  startPhone(request: StartPhoneOtpRequest): Observable<void> {
    return this.http.post<void>(`${this.base}/auth/phone/start`, request);
  }

  verifyPhone(request: VerifyPhoneOtpRequest): Observable<PhoneVerifyResult> {
    return this.http
      .post<Me | EmailCheckRequired>(`${this.base}/auth/phone/verify`, request, { observe: 'response' })
      .pipe(
        map((response): PhoneVerifyResult =>
          response.status === 202
            ? { kind: 'emailCheckRequired', check: response.body as EmailCheckRequired }
            : { kind: 'signedIn', me: response.body as Me },
        ),
      );
  }

  startEmailLogin(request: StartEmailLoginRequest): Observable<EmailLoginStarted> {
    return this.http.post<EmailLoginStarted>(`${this.base}/auth/email/start`, request);
  }

  completeEmailLogin(request: CompleteEmailLoginRequest): Observable<Me> {
    return this.http.post<Me>(`${this.base}/auth/email/complete`, request);
  }

  signOut(): Observable<void> {
    return this.http.post<void>(`${this.base}/auth/signout`, null);
  }

  getMe(): Observable<Me> {
    return this.http.get<Me>(`${this.base}/me`);
  }

  addEmail(email: string): Observable<void> {
    return this.http.post<void>(`${this.base}/me/email`, { email });
  }

  confirmEmail(request: ConfirmEmailRequest): Observable<Me> {
    return this.http.post<Me>(`${this.base}/me/email/confirm`, request);
  }

  removeEmail(): Observable<Me> {
    return this.http.delete<Me>(`${this.base}/me/email`);
  }
}
