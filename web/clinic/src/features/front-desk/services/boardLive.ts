import { HttpError, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { csrf } from '../../schedule/services/scheduleApi';
import { retryDelay, type LiveStatus } from '../domain/board';

type Handlers = {
  /** Something changed (the board: ids and state only; the queue TV: nothing at all): the caller refetches. */
  onChange: () => void;
  /** Joined, first time or again: anything missed meanwhile must be fetched now. */
  onResync: () => void;
  onStatus: (status: LiveStatus) => void;
};

export const boardHubPath = '/v1/hubs/board';

// The hubs' refusal texts (BoardHub.Refused, QueueHub): no access, so trying again cannot help.
const refusals = ['Not allowed at this facility.', 'Not a display.'];

/** 401/403 on connecting, or the hub refusing the facility: stop and leave it to the board's own refresh to show why. */
const isRefusal = (e: unknown) =>
  (e instanceof HttpError && (e.statusCode === 401 || e.statusCode === 403)) || (e instanceof Error && refusals.some(r => e.message.includes(r)));

/**
 * Live board updates for one facility (MDV-P1-029) over the SignalR hub. The session cookie authenticates; the URL and the
 * messages carry no patient data. Returns a function that closes the connection.
 */
export const watchBoard = (facilityId: string, handlers: Handlers) => watchHub(boardHubPath, 'BoardChanged', ['Watch', facilityId], handlers);

/**
 * A live connection to a hub: joins with the `join` call, then hears `event`. While not live the status is "paused" and the
 * caller polls; after every (re)join the caller refetches, so a change made while disconnected is never lost, and since the
 * caller always replaces its data whole nothing is shown twice. Stops for good when refused (401/403 or a refusal text).
 */
export function watchHub(path: string, event: string, [method, ...args]: [string, ...unknown[]], { onChange, onResync, onStatus }: Handlers): () => void {
  // The CSRF header rides on negotiate and long-polling requests (ADR-004); WebSocket upgrades are GETs, and the SameSite=Strict
  // cookies are never sent on another site's WebSocket.
  const connection = new HubConnectionBuilder()
    .withUrl(path, { headers: csrf, withCredentials: true })
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: ctx => retryDelay(ctx.previousRetryCount) })
    .configureLogging(LogLevel.None)
    .build();
  let stopped = false;
  let attempt = 0;
  let starting = false;
  let timer: ReturnType<typeof setTimeout> | undefined;

  const retry = () => {
    clearTimeout(timer);
    if (!stopped) timer = setTimeout(() => void start(), retryDelay(attempt++));
  };
  const join = async () => {
    await connection.invoke(method, ...args);
    attempt = 0;
    onStatus('live');
    onResync();
  };
  const start = async () => {
    if (stopped) return;
    starting = true;
    try {
      if (connection.state === HubConnectionState.Disconnected) await connection.start();
      await join();
    } catch (e) {
      if (stopped) return;
      onStatus('paused');
      if (isRefusal(e)) {
        stopped = true; // polling carries on and shows the board's own 401/403 state
        await connection.stop().catch(() => undefined);
        return;
      }
      await connection.stop().catch(() => undefined);
      retry();
    } finally {
      starting = false;
    }
  };

  connection.on(event, onChange);
  connection.onreconnecting(() => onStatus('paused'));
  connection.onreconnected(() => void join().catch(() => void connection.stop()));
  // Automatic reconnect gives up only when the server ends the connection (e.g. a session ended) or a rejoin failed: start over.
  connection.onclose(() => {
    if (stopped || starting) return; // a failed start schedules its own retry
    onStatus('paused');
    retry();
  });

  onStatus('connecting');
  void start();
  return () => {
    stopped = true;
    clearTimeout(timer);
    void connection.stop();
  };
}
