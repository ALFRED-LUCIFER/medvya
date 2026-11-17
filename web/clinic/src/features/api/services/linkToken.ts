/**
 * The token of an emailed or texted link (/invite#t=…, /rx#t=…), read from the page's fragment: a fragment never reaches a server log.
 * It is taken once and the address bar drops it at once (history.replaceState), so it is not in the history or a screenshot; later
 * renders of the same page get the kept copy.
 */
let taken: { path: string; token: string } | null = null;

export function takeLinkToken(): string {
  const match = /^#t=([A-Za-z0-9_-]{1,128})$/.exec(window.location.hash);
  if (match) {
    taken = { path: window.location.pathname, token: match[1]! };
    window.history.replaceState(window.history.state, '', window.location.pathname + window.location.search);
  }
  return taken?.path === window.location.pathname ? taken.token : '';
}
