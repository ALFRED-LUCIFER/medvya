// MDV-P1-102: the doctor's follow-up as the front desk sees it (FollowUpDue in src/Modules/Scheduling/Medvya.Scheduling.Application/IWaitlistService.cs):
// the waitlist entry to book or dismiss, the day it is due from and the doctor. Never the period, the reason or a note.
export type FollowUpDue = { id: string; dueOn: string; calendarId: string | null; calendarName: string | null };

/** Why the desk takes a follow-up off its list without booking it: codes, as the server's FollowUpDismissReason. */
export const dismissReasons = ['PatientDeclined', 'BookedElsewhere', 'NotReachable', 'Other'] as const;
export type DismissReason = (typeof dismissReasons)[number];

/** The schedule on the due day with the patient carried to the next slot chosen and the doctor's column only. Opaque ids and a date, never patient data. */
export const bookFollowUpPath = (schedulePath: string, patientId: string, f: FollowUpDue) => {
  const q = new URLSearchParams({ date: f.dueOn, view: 'day', patient: patientId });
  if (f.calendarId && f.calendarName) q.set('doctor', f.calendarId); // no name: the doctor no longer works here, so every column shows
  return `${schedulePath}?${q}`;
};
