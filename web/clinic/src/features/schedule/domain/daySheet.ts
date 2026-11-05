import type { AppointmentState, VisitType } from './schedule';

// POST /v1/schedule/print (src/Modules/Scheduling/Medvya.Scheduling.Application/DaySheet.cs): the only fields that may go on paper.
// No phone number, national or insurance ID, reason for visit or patient id. Add a field here only with the privacy owner's say-so.
export type DaySheetRow = { start: string; firstName: string; visitType: VisitType; state: AppointmentState };
export type DaySheetPage = { calendarId: string; doctorName: string; rows: DaySheetRow[] };
export type DaySheet = { facilityId: string; timeZone: string; date: string; printedAt: string; pages: DaySheetPage[] };
