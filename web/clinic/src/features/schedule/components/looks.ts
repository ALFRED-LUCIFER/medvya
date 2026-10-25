import { scheduleLooks, type Look } from '@medvya/ui';
import type { AppointmentState } from '../domain/schedule';
import { lookOf } from '../domain/views';

/** Unavailable time is hatched: a shape cue beside the icon and words, never colour alone. Token colours only. */
export const hatched = 'bg-[repeating-linear-gradient(45deg,var(--color-muted)_0_6px,var(--color-line)_6px_12px)]';

/** Colours, border and icon of an appointment state; the words come from the state itself. */
export const stateLook = (state: AppointmentState): Look => scheduleLooks[lookOf(state)];
