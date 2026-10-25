import { HttpError, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { csrf } from '../../schedule/services/scheduleApi';
import { retryDelay, type LiveStatus } from '../domain/board';

type Handlers = {
  /** A visit changed (ids and state only): the caller refetches the board. */
  onChange: () => void;
  /** Joined, first time or again: anything missed meanwhile must be fetched now. */
  onResync: () => void;
  onStatus: (status: LiveStatus) => void;
};

export const boardHubPath = '/v1/hubs/board';

// The hub's one refusal text (BoardHub.Refused): no access at this facility, so trying again cannot help.
const refused = 'Not allowed at this facility.';

/** 401/403 on connecting, or the hub refusing the facility: stop and leave it to the board's own refresh to show why. */
const isRefusal = (e: unknown) =>
  (e instanceof HttpError && (e.statusCode === 401 || e.statusCode === 403)) || (e instanceof Error && e.message.includes(refused));

/**
 * Live board updates for one facility (MDV-P1-029) over the SignalR hub. The session cookie authenticates; the URL and the
 * messages carry no patient data. While not live the status is "paused" and the caller polls; after every (re)join the caller
 * refetches, so a change made while disconnected is never lost, and since the board is always replaced whole nothing is shown
 * twice. Returns a function that closes the connection.
 */
export function watchBoard(facilityId: string, { onChange, onResync, onStatus }: Handlers): () => void {
  // The CSRF header rides on negotiate and long-polling requests (ADR-004); WebSocket upgrades are GETs, and the SameSite=Strict
  // session cookie is never sent on another site's WebSocket.
  const connection = new HubConnectionBuilder()
    .withUrl(boardHubPath, { headers: csrf, withCredentials: true })
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
    await connection.invoke('Watch', facilityId);
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

  connection.on('BoardChanged', onChange);
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
