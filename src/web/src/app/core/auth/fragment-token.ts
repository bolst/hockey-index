/**
 * Reads the magic-link token from `#t=…` and removes the fragment from the address bar and history
 * (plan R15), so the token never lands in history, referrers, or screenshots. Callers MUST call this
 * before sending the token anywhere.
 */
export function takeFragmentToken(window: Window): string | null {
  const { location, history } = window;
  const fragment = location.hash.startsWith('#') ? location.hash.slice(1) : location.hash;
  if (!fragment) {
    return null;
  }
  history.replaceState(history.state, '', `${location.pathname}${location.search}`);
  const token = new URLSearchParams(fragment).get('t');
  return token ? token : null;
}
