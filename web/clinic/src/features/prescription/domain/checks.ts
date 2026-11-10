import { z } from 'zod';

// Mirrors src/Modules/Prescribing/Medvya.Prescribing.Domain/SafetyChecks.cs and RxCheckView in IPrescriptions.cs (MDV-P2-016).
// The server runs every check; the screen only puts the result into words.
export type Severity = 'Severe' | 'Moderate';
export type AlertKind = 'Allergy' | 'CrossSensitivity' | 'Interaction' | 'Duplicate' | 'DoseAboveMaximum' | 'AllergiesNotRecorded';
export type NotCheckedReason = 'FreeText' | 'NoIngredientCode' | 'NotOnFormulary' | 'DoseNotChecked';
export type SafetyAlert = {
  key: string; kind: AlertKind; severity: Severity; itemId: string | null; medicine: string | null; other: string | null; otherOnChart: boolean;
  code: string | null; substance: string | null; allergyStatus: string | null; dailyMicrograms: number | null; maxDailyMicrograms: number | null;
};
export type NotChecked = { id: string; medicine: string; onChart: boolean; reason: NotCheckedReason };
export type RxOverride = { key: string; reason: string; ruleSetVersion: string; overriddenBy: string; overriddenAt: string };
export type RxChecks = {
  ruleSet: string; ruleSetVersion: string; allergies: 'NotRecorded' | 'NoKnownAllergies' | 'Listed';
  alerts: SafetyAlert[]; notChecked: NotChecked[]; overrides: RxOverride[]; unresolved: number; checkedAt: string;
};

export const overrideSchema = z.object({ reason: z.string().trim().min(1).max(200) });

/** A daily amount in the unit a doctor reads: mg from 1 mg up, else mcg. */
export const amountOf = (micrograms: number) =>
  micrograms >= 1000 ? { unit: 'mg' as const, value: String(micrograms / 1000) } : { unit: 'mcg' as const, value: String(micrograms) };

/** How the alert is painted: a severe one without a reason is critical; once overridden it is information; moderate is attention. */
export const lookOf = (a: SafetyAlert, overridden: boolean) => (a.severity === 'Severe' ? (overridden ? 'info' : 'critical') : 'attention');

/**
 * The per-line safety chip: the worst open finding on the line, then an overridden one, then whether anything about it went unchecked
 * (allergies not recorded count: "no alerts" is never said when the allergy check could not run).
 */
export type LineSafety = 'severe' | 'moderate' | 'overridden' | 'notChecked' | 'clear';
export function lineSafety(checks: RxChecks, itemId: string): LineSafety {
  const mine = checks.alerts.filter(a => a.itemId === itemId);
  const open = mine.filter(a => !checks.overrides.some(o => o.key === a.key));
  if (open.some(a => a.severity === 'Severe')) return 'severe';
  if (open.some(a => a.severity === 'Moderate')) return 'moderate';
  if (mine.length > 0) return 'overridden';
  return checks.allergies === 'NotRecorded' || checks.notChecked.some(n => n.id === itemId) ? 'notChecked' : 'clear';
}
