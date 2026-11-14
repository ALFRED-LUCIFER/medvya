// GET /v1/front-desk/clinic-today (SchedulingEndpoints.ClinicTodayView, MDV-P2-053): counts and staff names only, never a patient.
export type ClinicTodayDoctor = { id: string; name: string | null; notesToSign: number; notesBeforeToday: number; toAuthorise: number };
export type ClinicToday = {
  now: string; timeZone: string;
  booked: number; toArrive: number; arrivedWalkIn: number; arrivedBooked: number;
  /** Null when nobody waits: shown as No data yet, never 0 minutes. */
  waiting: number; longestWaitMinutes: number | null;
  incompleteRegistrations: number;
  doctors: ClinicTodayDoctor[];
};

export type AttentionLine =
  | { kind: 'mine'; notes: number; prescriptions: number }
  | { kind: 'incomplete'; count: number }
  | { kind: 'waiting'; count: number; minutes: number };

/** Which screens the signed-in person may open: a line is listed only with the action that deals with it. */
export type AttentionScreens = { myDay: boolean; directory: boolean; waitingRoom: boolean };

/**
 * The needs-attention list (MDV-P2-053): each line has one action that opens the screen for it. The signed-in doctor's own notes and
 * prescriptions (My day), incomplete registrations (the directory's Incomplete filter), patients waiting (the board's waiting room).
 * shortcut: another doctor's backlog is only in the per-doctor counts, as no screen lets the owner act on it; add a line when a Remind action exists.
 */
export function attention(today: ClinicToday, userId: string, screens: AttentionScreens): AttentionLine[] {
  const mine = today.doctors.find(d => d.id === userId);
  const lines: AttentionLine[] = [];
  if (screens.myDay && mine && mine.notesToSign + mine.toAuthorise > 0) lines.push({ kind: 'mine', notes: mine.notesToSign, prescriptions: mine.toAuthorise });
  if (screens.directory && today.incompleteRegistrations > 0) lines.push({ kind: 'incomplete', count: today.incompleteRegistrations });
  if (screens.waitingRoom && today.waiting > 0 && today.longestWaitMinutes !== null) lines.push({ kind: 'waiting', count: today.waiting, minutes: today.longestWaitMinutes });
  return lines;
}
