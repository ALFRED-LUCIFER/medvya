/** Patients see only signed summaries and reviewed results, in plain words (never AI drafts). */
export type VisitSummary = { id: string; date: string; title: string; summary: string; signedBy: string };
export type Result = { id: string; test: string; value: string; date: string; flag: 'normal' | 'high' | 'low'; doctorNote: string };
export type Medicine = { id: string; name: string; dose: string; changed?: boolean };
export type Records = { visits: VisitSummary[]; results: Result[]; medicines: Medicine[] };
export type RecordsTab = 'visits' | 'results' | 'medicines';
