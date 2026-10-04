import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { Me } from '../api/auth-api';
import { Session } from './session';

export const SIGN_IN_PATH = '/host/sign-in';

/** Set on the sign-in redirect when the session check failed for a reason other than 401. */
export const SESSION_UNAVAILABLE_PARAM = 'unavailable';

function signInRedirect(router: Router, returnUrl: string, unavailable = false): UrlTree {
  const queryParams = unavailable ? { returnUrl, [SESSION_UNAVAILABLE_PARAM]: 1 } : { returnUrl };
  return router.createUrlTree([SIGN_IN_PATH], { queryParams });
}

/** Resolves to the signed-in host, `null` when signed out, or a redirect when the API is unreachable. */
async function loadSession(router: Router, session: Session, returnUrl: string): Promise<Me | null | UrlTree> {
  try {
    return await session.ensureLoaded();
  } catch {
    return signInRedirect(router, returnUrl, true);
  }
}

export const hostGuard: CanActivateFn = async (_route, state) => {
  const router = inject(Router);
  const me = await loadSession(router, inject(Session), state.url);
  if (me instanceof UrlTree) {
    return me;
  }
  return me ? true : signInRedirect(router, state.url);
};

export const adminGuard: CanActivateFn = async (_route, state) => {
  const router = inject(Router);
  const me = await loadSession(router, inject(Session), state.url);
  if (me instanceof UrlTree) {
    return me;
  }
  if (!me) {
    return signInRedirect(router, state.url);
  }
  return me.isAdmin ? true : router.createUrlTree(['/host']);
};

/** Accepts only same-app paths so `returnUrl` cannot become an open redirect. */
export function safeReturnUrl(candidate: string | null | undefined, fallback = '/host'): string {
  if (!candidate || !candidate.startsWith('/') || candidate.startsWith('//') || candidate.includes('\\')) {
    return fallback;
  }
  return candidate;
}
