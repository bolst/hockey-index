import { ApiProblem } from '../../../core/api/api-problem';

const PROBLEM_MESSAGES: Readonly<Record<string, string>> = {
  active_limit_reached:
    'You have reached your limit of active events. Cancel an event or wait for one to end before you publish another.',
  daily_publish_limit_reached: 'You have reached your daily publish limit. Try again tomorrow.',
  host_inactive: 'Your host account is not active, so you cannot publish or change events.',
  event_ended: 'This event has already ended, so you cannot publish it. Change the dates or duplicate it.',
  event_not_editable: 'This event can no longer be changed.',
  venue_not_found: 'That venue no longer exists. Choose another venue.',
};

export const PUBLISH_PARKED_MESSAGE =
  'Link checking is delayed. Your event publishes automatically once the links pass.';
export const EDIT_PARKED_MESSAGE = 'Link checking is delayed. Your new join instructions go live once the links pass.';

export function problemMessage(problem: ApiProblem): string {
  return (problem.code && PROBLEM_MESSAGES[problem.code]) || problem.title;
}

export function blockedUrlsOf(problem: ApiProblem): string[] {
  const urls = problem.body['blockedUrls'];
  return Array.isArray(urls) ? urls.filter((url): url is string => typeof url === 'string') : [];
}
