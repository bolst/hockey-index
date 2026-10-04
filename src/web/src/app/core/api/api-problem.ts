import { HttpErrorResponse } from '@angular/common/http';

/** RFC 9457 Problem Details as returned by the API; `code` is the API's machine-readable extension. */
export interface ApiProblem {
  status: number;
  code: string | null;
  title: string;
  detail: string | null;
  body: Record<string, unknown>;
}

const NETWORK_FAILURE_TITLE = 'Could not reach Hockey Index. Check your connection and try again.';
const FALLBACK_TITLE = 'Something went wrong. Try again.';

export function toApiProblem(error: unknown): ApiProblem {
  if (!(error instanceof HttpErrorResponse)) {
    return { status: 0, code: null, title: FALLBACK_TITLE, detail: null, body: {} };
  }
  if (error.status === 0) {
    return { status: 0, code: null, title: NETWORK_FAILURE_TITLE, detail: null, body: {} };
  }
  const body: Record<string, unknown> =
    error.error !== null && typeof error.error === 'object' ? (error.error as Record<string, unknown>) : {};
  return {
    status: error.status,
    code: typeof body['code'] === 'string' ? body['code'] : null,
    title: typeof body['title'] === 'string' ? body['title'] : FALLBACK_TITLE,
    detail: typeof body['detail'] === 'string' ? body['detail'] : null,
    body,
  };
}
