import { toApiProblem } from '../../core/api/api-problem';

const MESSAGES_BY_CODE: Record<string, string> = {
  host_banned: 'This host is already banned.',
  host_not_banned: 'This host is not banned.',
  cannot_ban_self: 'You cannot ban your own account.',
  blocklist_duplicate: 'That value is already on the blocklist.',
  venue_merge_invalid: 'These venues cannot be merged. Check the target venue ID; it must be a different, existing venue.',
};

/** User-facing message for a failed admin call. `notFound` replaces the API title on 404. */
export function adminErrorMessage(error: unknown, notFound?: string): string {
  const problem = toApiProblem(error);
  if (problem.status === 404 && notFound) {
    return notFound;
  }
  return (problem.code && MESSAGES_BY_CODE[problem.code]) || problem.title;
}
