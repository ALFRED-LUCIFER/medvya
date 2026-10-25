import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// A stand-in for the SignalR client: each test scripts what start() and invoke() do and fires the connection's events.
const hub = vi.hoisted(() => {
  class HttpError extends Error {
    statusCode: number;
    constructor(message: string, statusCode: number) {
      super(message);
      this.statusCode = statusCode;
    }
  }
  const fake = {
    state: 'Disconnected',
    start: vi.fn<() => Promise<void>>(),
    stop: vi.fn<() => Promise<void>>(),
    invoke: vi.fn<(method: string, ...args: unknown[]) => Promise<void>>(),
    handlers: {} as Record<string, () => void>,
    reconnecting: () => {},
    reconnected: () => {},
    closed: () => {},
  };
  return { HttpError, fake };
});

vi.mock('@microsoft/signalr', () => {
  const { fake, HttpError } = hub;
  const connection = {
    get state() { return fake.state; },
    start: () => fake.start(),
    stop: () => fake.stop(),
    invoke: (m: string, ...a: unknown[]) => fake.invoke(m, ...a),
    on: (name: string, f: () => void) => { fake.handlers[name] = f; },
    onreconnecting: (f: () => void) => { fake.reconnecting = f; },
    onreconnected: (f: () => void) => { fake.reconnected = f; },
    onclose: (f: () => void) => { fake.closed = f; },
  };
  class HubConnectionBuilder {
    withUrl() { return this; }
    withAutomaticReconnect() { return this; }
    configureLogging() { return this; }
    build() { return connection; }
  }
  return { HubConnectionBuilder, HttpError, HubConnectionState: { Disconnected: 'Disconnected' }, LogLevel: { None: 6 } };
});

const { watchBoard } = await import('./boardLive');
const { fake, HttpError } = hub;

describe('live board connection', () => {
  let statuses: string[];
  const onChange = vi.fn(), onResync = vi.fn();
  const watch = () => watchBoard('f-1', { onChange, onResync, onStatus: s => statuses.push(s) });

  beforeEach(() => {
    vi.useFakeTimers();
    statuses = [];
    onChange.mockReset();
    onResync.mockReset();
    fake.state = 'Disconnected';
    fake.start.mockReset().mockImplementation(async () => { fake.state = 'Connected'; });
    fake.stop.mockReset().mockImplementation(async () => { fake.state = 'Disconnected'; });
    fake.invoke.mockReset().mockResolvedValue(undefined);
  });
  afterEach(() => vi.useRealTimers());

  it('joins its facility, refetches once joined and on every change', async () => {
    watch();
    await vi.runAllTimersAsync();
    expect(fake.invoke).toHaveBeenCalledWith('Watch', 'f-1');
    expect(statuses).toEqual(['connecting', 'live']);
    expect(onResync).toHaveBeenCalledTimes(1);
    fake.handlers.BoardChanged!();
    expect(onChange).toHaveBeenCalledTimes(1);
  });

  it('pauses on a drop and refetches after rejoining, so nothing missed meanwhile is lost', async () => {
    watch();
    await vi.runAllTimersAsync();
    fake.reconnecting();
    expect(statuses.at(-1)).toBe('paused');
    fake.reconnected();
    await vi.runAllTimersAsync();
    expect(fake.invoke).toHaveBeenCalledTimes(2); // joined again
    expect(statuses.at(-1)).toBe('live');
    expect(onResync).toHaveBeenCalledTimes(2);
  });

  it('keeps retrying with back-off while the server is unreachable, then resyncs', async () => {
    fake.start.mockRejectedValueOnce(new HttpError('Bad Gateway', 502)).mockRejectedValueOnce(new Error('network'));
    watch();
    await vi.advanceTimersByTimeAsync(0);
    expect(statuses).toEqual(['connecting', 'paused']);
    await vi.advanceTimersByTimeAsync(500); // first retry after 0.5 s, fails again
    expect(fake.start).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(999);
    expect(fake.start).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(1); // second retry after 1 s, succeeds
    expect(fake.start).toHaveBeenCalledTimes(3);
    expect(statuses.at(-1)).toBe('live');
    expect(onResync).toHaveBeenCalledTimes(1);
  });

  it('a server-ended connection starts over', async () => {
    watch();
    await vi.runAllTimersAsync();
    fake.state = 'Disconnected';
    fake.closed();
    expect(statuses.at(-1)).toBe('paused');
    await vi.runAllTimersAsync();
    expect(fake.start).toHaveBeenCalledTimes(2);
    expect(statuses.at(-1)).toBe('live');
  });

  it.each([
    ['signed out', new HttpError('Unauthorized', 401)],
    ['no board access', new HttpError('Forbidden', 403)],
  ])('stops for good when refused on connecting (%s)', async (_, error) => {
    fake.start.mockRejectedValue(error);
    watch();
    await vi.runAllTimersAsync();
    expect(statuses).toEqual(['connecting', 'paused']);
    expect(fake.start).toHaveBeenCalledTimes(1);
  });

  it('stops for good when the hub refuses the facility', async () => {
    fake.invoke.mockRejectedValue(new Error("An unexpected error occurred invoking 'Watch' on the server. HubException: Not allowed at this facility."));
    watch();
    await vi.runAllTimersAsync();
    expect(statuses).toEqual(['connecting', 'paused']);
    expect(fake.start).toHaveBeenCalledTimes(1);
    expect(onResync).not.toHaveBeenCalled();
  });

  it('closing stops the connection and every retry', async () => {
    fake.start.mockRejectedValue(new Error('network'));
    const close = watch();
    await vi.advanceTimersByTimeAsync(0);
    close();
    await vi.runAllTimersAsync();
    expect(fake.start).toHaveBeenCalledTimes(1);
    expect(fake.stop).toHaveBeenCalled();
  });
});
