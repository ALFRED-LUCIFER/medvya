import { watchHub } from '../../front-desk/services/boardLive';
import { csrf, post, send } from '../../schedule/services/scheduleApi';
import type { LiveStatus } from '../../front-desk/domain/board';
import { pairingToken, type CreatedDisplay, type Display, type QueueTv } from '../domain/queueTv';

/** Pairs this screen: the display token goes in the body, and the server answers with an HttpOnly display cookie. */
export const pairDisplay = (token: string) => post<void>('/v1/queue/tv/session', { token });

/** The facility's queue, for the paired display (its cookie); the facility is never in the request. */
export const fetchQueueTv = () => send<QueueTv>('/v1/queue/tv');

/** Live queue: an empty "changed" ping after every visit change; the caller refetches. */
export const watchQueue = (handlers: { onChange: () => void; onResync: () => void; onStatus: (s: LiveStatus) => void }) =>
  watchHub('/v1/queue/hub', 'QueueChanged', ['Watch'], handlers);

let taken: string | null | undefined;
/**
 * The display token in this page's fragment (/queue/tv#token), read once and removed from the address at once, so it stays out
 * of the history and off the screen. The fragment is never sent to a server.
 */
export function takePairingToken() {
  if (taken === undefined) {
    taken = pairingToken(window.location.hash);
    if (window.location.hash) window.history.replaceState(null, '', window.location.pathname);
  }
  return taken;
}

const chimeKey = 'medvya.queueTv.chime';
/** Whether this screen chimes: a per-screen choice, so browser storage is enough (no patient data). */
export const chimePreference = {
  read: () => {
    try {
      return localStorage.getItem(chimeKey) === 'on';
    } catch {
      return false;
    }
  },
  save: (on: boolean) => {
    try {
      localStorage.setItem(chimeKey, on ? 'on' : 'off');
    } catch {
      // storage blocked: the choice lasts until the page reloads
    }
  },
};

/** Two soft tones (no audio file). Browsers allow sound only after someone pressed a button on the screen once. */
export function playChime() {
  try {
    const audio = new AudioContext();
    [660, 880].forEach((hz, i) => {
      const tone = audio.createOscillator(), gain = audio.createGain();
      tone.frequency.value = hz;
      gain.gain.setValueAtTime(0.0001, audio.currentTime + i * 0.35);
      gain.gain.exponentialRampToValueAtTime(0.3, audio.currentTime + i * 0.35 + 0.05);
      gain.gain.exponentialRampToValueAtTime(0.0001, audio.currentTime + i * 0.35 + 0.6);
      tone.connect(gain).connect(audio.destination);
      tone.start(audio.currentTime + i * 0.35);
      tone.stop(audio.currentTime + i * 0.35 + 0.6);
    });
    setTimeout(() => void audio.close(), 1500);
  } catch {
    // no audio on this screen: the board still updates
  }
}

// Staff: displays of the active facility. Ids only in URLs.
export const fetchDisplays = (facilityId: string) => send<Display[]>(`/v1/queue/displays?${new URLSearchParams({ facilityId })}`);
export const createDisplay = (facilityId: string, name: string) => post<CreatedDisplay>('/v1/queue/displays', { facilityId, name });
export const revokeDisplay = (id: string) => send<void>(`/v1/queue/displays/${id}`, { method: 'DELETE', headers: csrf });

/** The link to open once on the TV; the token rides in the fragment. */
export const displayLink = (token: string) => `${window.location.origin}/queue/tv#${token}`;

export const copyText = (text: string) => navigator.clipboard.writeText(text);
