import { HttpInterceptorFn } from '@angular/common/http';
import { environment } from '../../../environments/environment';

const CREDENTIALED_SEGMENTS: ReadonlySet<string> = new Set([
  'auth',
  'me',
  'host',
  'admin',
  'venues',
]);

function isCredentialedApiUrl(rawUrl: string): boolean {
  const base = new URL(environment.apiBaseUrl);
  let url: URL;
  try {
    url = new URL(rawUrl, base);
  } catch {
    return false;
  }
  const basePath = base.pathname.replace(/\/$/, '');
  if (url.origin !== base.origin || !url.pathname.startsWith(`${basePath}/`)) {
    return false;
  }
  const [firstSegment] = url.pathname.slice(basePath.length + 1).split('/');
  return CREDENTIALED_SEGMENTS.has(firstSegment);
}

export const credentialsInterceptor: HttpInterceptorFn = (request, next) =>
  next(
    isCredentialedApiUrl(request.url)
      ? request.clone({
          withCredentials: true,
          setHeaders: { 'X-HI-Requested-With': 'hockey-index' },
        })
      : request,
  );
