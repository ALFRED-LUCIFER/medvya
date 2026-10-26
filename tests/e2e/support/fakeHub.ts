import { expect, type Page } from '@playwright/test';

/**
 * A stand-in for a SignalR hub at `path` (the board, MDV-P1-029; the queue TV, MDV-P1-031), speaking the JSON protocol over
 * long polling, the client's fallback transport. It answers Watch, can push any message, and can drop the connection and refuse
 * to reconnect until restored. `forbidden` text must never appear in the hub's URLs.
 */
export async function fakeHub(page: Page, path: string, forbidden: RegExp) {
  const RS = '\u001e';
  let queue: string[] = [];
  let waiting: ((body: string | null) => void) | null = null;
  let down = false;
  const hub = { watched: [] as unknown[][], negotiations: 0 };
  const flush = () => {
    if (waiting && queue.length) {
      const send = waiting;
      waiting = null;
      send(queue.join(''));
      queue = [];
    }
  };
  const emit = (message: object) => { queue.push(JSON.stringify(message) + RS); flush(); };
  await page.route(`**${path}/negotiate*`, r => {
    if (down) return r.fulfill({ status: 503, body: '' });
    hub.negotiations++;
    return r.fulfill({ json: { negotiateVersion: 1, connectionId: `c${hub.negotiations}`, connectionToken: `k${hub.negotiations}`,
      availableTransports: [{ transport: 'LongPolling', transferFormats: ['Text', 'Binary'] }] } });
  });
  let firstPoll = true;
  await page.route(`**${path}?*`, async r => {
    const request = r.request();
    expect(request.url()).not.toMatch(forbidden);
    if (request.method() === 'DELETE') return r.fulfill({ status: 202, body: '' });
    if (request.method() === 'POST') {
      for (const frame of (request.postData() ?? '').split(RS).filter(Boolean)) {
        const message = JSON.parse(frame) as { protocol?: string; type?: number; target?: string; invocationId?: string; arguments?: unknown[] };
        if (message.protocol) emit({}); // handshake
        else if (message.type === 1 && message.target === 'Watch') {
          hub.watched.push(message.arguments ?? []);
          emit({ type: 3, invocationId: message.invocationId, result: null });
        }
      }
      return r.fulfill({ status: 200, body: '' });
    }
    if (firstPoll) { firstPoll = false; return r.fulfill({ status: 200, body: '' }); } // the long-polling transport's opening poll
    const body = await new Promise<string | null>(resolve => { waiting = resolve; flush(); });
    return body === null ? r.fulfill({ status: 204, body: '' }) : r.fulfill({ status: 200, body });
  });
  return Object.assign(hub, {
    push: (target: string, args: unknown[] = []) => emit({ type: 1, target, arguments: args }),
    /** Ends the connection from the server side and refuses new ones until restored. */
    drop: () => { down = true; firstPoll = true; waiting?.(null); waiting = null; queue = []; },
    restore: () => { down = false; },
  });
}
