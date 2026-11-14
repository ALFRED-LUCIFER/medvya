import { scheduleLooks, type Look, type Tone } from '@medvya/ui';
import type { AppointmentState } from '../domain/schedule';
import { lookOf, type StateLook } from '../domain/views';

/** Unavailable time is hatched: a shape cue beside the icon and words, never colour alone. Token colours only. */
export const hatched = 'bg-[repeating-linear-gradient(45deg,var(--color-muted)_0_6px,var(--color-line)_6px_12px)]';

/** Colours, border and icon of an appointment state; the words come from the state itself. */
export const stateLook = (state: AppointmentState): Look => scheduleLooks[lookOf(state)];

/** The library tone of each state's chip (MDV-P2-060); the words and icon say which state. */
const tones: Record<StateLook, Exclude<Tone, 'ai'>> = { booked: 'info', checkedIn: 'info', inRoom: 'brand', completed: 'neutral', noShow: 'danger', cancelled: 'neutral', needsRebooking: 'warning' };
export const stateTone = (state: AppointmentState): Exclude<Tone, 'ai'> => tones[lookOf(state)];
