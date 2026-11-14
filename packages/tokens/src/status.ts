/**
 * One status vocabulary for every app (MDV-P5-057): the words (an i18n key), the tone, and one glyph drawn
 * from each icon set, Lucide on web (`web`) and Material on native (`native`), chosen to be the same shape.
 * A status is always glyph and words, never colour alone (AGENTS.md 1.5).
 */
export type StatusTone = 'danger' | 'warning' | 'success' | 'info' | 'ai' | 'neutral'; // never brand: DESIGN.md keeps it off status

export const statuses = {
  // Visit states, named as the server names them.
  Booked: { label: 'schedule.state.Booked', tone: 'info', web: 'CalendarCheck', native: 'event-available' },
  CheckedIn: { label: 'schedule.state.CheckedIn', tone: 'info', web: 'UserCheck', native: 'how-to-reg' },
  WithNurse: { label: 'schedule.state.WithNurse', tone: 'info', web: 'Syringe', native: 'vaccines' },
  ReadyForDoctor: { label: 'schedule.state.ReadyForDoctor', tone: 'info', web: 'ClipboardCheck', native: 'assignment-turned-in' },
  InConsult: { label: 'schedule.state.InConsult', tone: 'info', web: 'DoorOpen', native: 'meeting-room' },
  Billing: { label: 'schedule.state.Billing', tone: 'neutral', web: 'ReceiptText', native: 'receipt-long' },
  Done: { label: 'schedule.state.Done', tone: 'success', web: 'CircleCheck', native: 'check-circle' },
  // Clinical flags and provenance. Allergy has its own glyph: never the medicine glyph, never the critical one.
  allergy: { label: 'patientBanner.allergy', tone: 'danger', web: 'ShieldAlert', native: 'gpp-maybe' },
  critical: { label: 'alert.critical', tone: 'danger', web: 'CircleAlert', native: 'error' },
  attention: { label: 'alert.attention', tone: 'warning', web: 'TriangleAlert', native: 'warning-amber' },
  verified: { label: 'provenance.verified', tone: 'success', web: 'BadgeCheck', native: 'verified' },
  ai: { label: 'ai.badge', tone: 'ai', web: 'Sparkles', native: 'auto-awesome' },
  signed: { label: 'provenance.signed', tone: 'success', web: 'Lock', native: 'lock' },
} as const satisfies Record<string, { label: string; tone: StatusTone; web: string; native: string }>;

export type Status = keyof typeof statuses;
export type WebGlyph = (typeof statuses)[Status]['web'];

/** The medicine glyph in each set: no status may use it, so an allergy never looks like a medicine. */
export const medicineGlyph = { web: 'Pill', native: 'medication' } as const;
