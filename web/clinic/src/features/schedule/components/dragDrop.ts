import type { Appointment } from '../domain/schedule';

/** Opens a visit's detail panel. */
export type OpenHandler = (appointment: Appointment, calendarId: string, calendarName: string) => void;
/** A booked visit dropped on an empty slot (drag to reschedule; the panel then asks before moving). */
export type DropHandler = (appointment: Appointment, from: { calendarId: string; calendarName: string }, to: { calendarId: string; calendarName: string; start: string }) => void;
/** What the drag carries: the visit's id only, never patient data. */
export const dragType = 'application/x-medvya-appointment';
