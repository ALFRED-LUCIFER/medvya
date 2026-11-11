import { z } from 'zod';

// Mirrors src/Modules/Prescribing/Medvya.Prescribing.Domain/SafetyChecks.cs and RxCheckView in IPrescriptions.cs (MDV-P2-016, MDV-P2-040).
// The server runs every check; the screen only puts the result into words.
export type Severity = 'Severe' | 'Moderate';
export type AlertKind =
  | 'Allergy' | 'CrossSensitivity' | 'Interaction' | 'Duplicate' | 'DoseAboveMaximum' | 'AllergiesNotRecorded' | 'Pregnancy' | 'ChildDose' | 'Kidney' | 'OlderAdult';
export type NotCheckedReason = 'FreeText' | 'NoIngredientCode' | 'NotOnFormulary' | 'DoseNotChecked' | 'NoRecentWeight' | 'NoEgfr' | 'PregnancyNotRecorded';
export type SafetyAlert = {
  key: string; kind: AlertKind; severity: Severity; itemId: string | null; medicine: string | null; other: string | null; otherOnChart: boolean;
  code: string | null; substance: string | null; allergyStatus: string | null; dailyMicrograms: number | null; maxDailyMicrograms: number | null;
  /** The patient fact a patient rule rests on: gestation weeks, eGFR or age. */
  measure: number | null;
};
/** A child's dose worked out from the weight: weight in hundredths of a kg, the dose in micrograms and, for a liquid, tenths of a mL. */
export type WeightDose = {
  itemId: string; medicine: string; weightHundredthsKg: number; mgPerKg: number; doseMicrograms: number; doseTenthsMl: number | null;
  maxDosesPerDay: number; maxDailyMicrograms: number;
};
export type NotChecked = { id: string; medicine: string; onChart: boolean; reason: NotCheckedReason };
export type RxOverride = { key: string; reason: string; ruleSetVersion: string; overriddenBy: string; overriddenAt: string };
export type RxChecks = {
  ruleSet: string; ruleSetVersion: string; allergies: 'NotRecorded' | 'NoKnownAllergies' | 'Listed';
  alerts: SafetyAlert[]; notChecked: NotChecked[]; overrides: RxOverride[]; unresolved: number; checkedAt: string; weightDoses: WeightDose[];
};

export const overrideSchema = z.object({ reason: z.string().trim().min(1).max(200) });

/** A daily amount in the unit a doctor reads: mg from 1 mg up, else mcg. */
export const amountOf = (micrograms: number) =>
  micrograms >= 1000 ? { unit: 'mg' as const, value: String(micrograms / 1000) } : { unit: 'mcg' as const, value: String(micrograms) };

/** A weight in hundredths of a kg, as the doctor reads it (1420 is "14.2"). */
export const kgOf = (hundredths: number) => String(hundredths / 100);

/** A volume in tenths of a mL (89 is "8.9"). */
export const mlOf = (tenths: number) => String(tenths / 10);

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
