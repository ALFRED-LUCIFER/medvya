import type { I18n, MessageKey } from '@medvya/i18n';

// The dental chart and plan (MDV-P8-011, MDV-P8-012) as the server answers them (DentalEndpoints). Ids, codes and minor units; words come from the catalogue.

export const conditions = ['Sound', 'Caries', 'Filling', 'Crown', 'RootCanalTreated', 'Missing', 'Implant', 'Bridge', 'Mobile', 'Fractured'] as const;
export type ToothCondition = (typeof conditions)[number];
export type PlanStatus = 'Planned' | 'InProgress' | 'Done' | 'Declined';
export type AcceptanceMethod = 'OnTablet' | 'PrintedAndSigned';

export type ToothFinding = {
  id: string; tooth: string; condition: ToothCondition; surfaces: string | null; note: string | null; encounterId: string;
  recordedBy: string; recordedAt: string; author: string | null;
};
export type PlanItem = {
  id: string; serviceId: string; serviceName: string; tooth: string | null; surfaces: string | null; phase: number; estimateMinor: number; currency: string;
  status: PlanStatus; reason: string | null; createdAt: string; changedAt: string; doneAt: string | null; version: number;
};
export type Acceptance = { id: string; acceptedBy: string; method: AcceptanceMethod; totalMinor: number; currency: string; acceptedAt: string; recordedBy: string };
export type DentalPlan = { items: PlanItem[]; totalMinor: number; currency: string | null; acceptance: Acceptance | null; acceptanceCurrent: boolean };
export type DentalChart = {
  teeth: ToothFinding[]; history: ToothFinding[]; plan: DentalPlan; openEncounterId: string | null; timeZone: string; rulesSignedOff: boolean;
};
export type DentalService = { id: string; name: string; code: string | null; durationMinutes: number | null; priceMinor: number | null; currency: string };

/** Caries and fillings are recorded on surfaces; every other condition is of the whole tooth (DentalRules.SurfaceConditions on the server). */
export const surfaceConditions: ReadonlySet<ToothCondition> = new Set(['Caries', 'Filling']);
export const surfaceOrder = ['M', 'D', 'O', 'B', 'L'] as const;
export type Surface = (typeof surfaceOrder)[number];

/** The library's tooth chart vocabulary (ToothChart in @medvya/ui), restated here because the domain is pure. */
export type Dentition = 'permanent' | 'primary' | 'mixed';
export type ConditionTone = 'danger' | 'warning' | 'success' | 'info' | 'neutral';
export type ToothMark = { abbr: string; label: string; tone: ConditionTone };

/** The tone each condition draws in; the code on the tooth carries the meaning, the tone only backs it up. */
export const conditionTone: Record<ToothCondition, ConditionTone> = {
  Sound: 'success', Caries: 'danger', Fractured: 'danger', Mobile: 'warning', Missing: 'neutral',
  Filling: 'info', Crown: 'info', RootCanalTreated: 'info', Implant: 'info', Bridge: 'info',
};

export const dentitions: readonly Dentition[] = ['permanent', 'primary', 'mixed'];

/** Surfaces in the server's order (M, D, O, B, L), whatever order they were picked in. */
export const sortSurfaces = (picked: Iterable<string>): string => surfaceOrder.filter(s => new Set(picked).has(s)).join('');

/** What the finding form may send: surfaces only for the conditions that take them, at least one for those. */
export function findingProblem(condition: ToothCondition | null, surfaces: string): 'condition' | 'surfaces:required' | null {
  if (!condition) return 'condition';
  if (surfaceConditions.has(condition) && surfaces.length === 0) return 'surfaces:required';
  return null;
}

/** The mark each recorded tooth gets on the chart: its code and its state in words. */
export function marksFor(teeth: readonly ToothFinding[], t: I18n['t']): Record<string, ToothMark> {
  return Object.fromEntries(teeth.map(f => [f.tooth, {
    abbr: t(`dental.condition.abbr.${f.condition}`),
    label: f.surfaces ? `${t(`dental.condition.${f.condition}`)} (${f.surfaces})` : t(`dental.condition.${f.condition}`),
    tone: conditionTone[f.condition],
  }]));
}

/** The next steps a dentist may take on an item, as the server allows them (DentalRules.CanMove). */
export function nextSteps(status: PlanStatus): ('start' | 'done' | 'decline' | 'offer')[] {
  switch (status) {
    case 'Planned': return ['start', 'decline'];
    case 'InProgress': return ['done', 'decline'];
    case 'Declined': return ['offer'];
    default: return [];
  }
}

export const stepTarget: Record<'start' | 'done' | 'decline' | 'offer', PlanStatus> = { start: 'InProgress', done: 'Done', decline: 'Declined', offer: 'Planned' };

/** Items by phase, in phase order; the server sends them in phase then creation order. */
export function byPhase(items: readonly PlanItem[]): { phase: number; items: PlanItem[] }[] {
  const phases = [...new Set(items.map(i => i.phase))].sort((a, b) => a - b);
  return phases.map(phase => ({ phase, items: items.filter(i => i.phase === phase) }));
}

/** The field errors the dental routes answer with ("field:code"), each with words in the catalogue (dental.error.field.code). */
export const dentalErrors = [
  'tooth:invalid', 'condition:invalid', 'surfaces:required', 'surfaces:not_allowed', 'surfaces:invalid', 'surfaces:needs_tooth', 'note:too_long',
  'encounter:ended', 'encounterId:required', 'encounterId:ended', 'encounterId:invalid', 'to:not_allowed', 'reason:required', 'reason:too_long',
  'serviceId:required', 'serviceId:unknown', 'serviceId:no_price', 'phase:range', 'acceptedBy:required', 'acceptedBy:too_long', 'method:invalid', 'plan:empty',
] as const;

/** A server field error as its catalogue key, or null when it is not one of the dental errors (the screen then says the generic failure). */
export const errorKey = (code: string | undefined): MessageKey | null =>
  code && (dentalErrors as readonly string[]).includes(code) ? (`dental.error.${code.replace(':', '.')}` as MessageKey) : null;

export const money = (minor: number, currency: string, format: (minor: number, currency: 'INR' | 'AED') => string) =>
  currency === 'INR' || currency === 'AED' ? format(minor, currency) : `${currency} ${(minor / 100).toFixed(2)}`;

/** What the Dental tab shows and does (the controller's view model, useDentalController). */
export type DentalView = {
  loading: boolean; unavailable: boolean; forbidden: boolean; failed: boolean; chart: DentalChart | null; canChange: boolean; encounterId: string | null;
  dentition: Dentition; onDentition: (d: Dentition) => void;
  tooth: string | null; onTooth: (tooth: string) => void; current: ToothFinding | null; history: ToothFinding[];
  condition: ToothCondition | null; onCondition: (c: ToothCondition) => void; surfaces: string; onSurface: (s: string) => void; note: string; onNote: (note: string) => void;
  findingProblem: 'condition' | 'surfaces:required' | null; findingSaving: boolean; findingError: MessageKey | null; findingFailed: boolean; findingAdded: string | null; onAddFinding: () => void;
  services: DentalService[]; servicesLoading: boolean; serviceId: string; onService: (id: string) => void; phase: number; onPhase: (phase: number) => void;
  planTooth: string | null; onPlanTooth: (tooth: string | null) => void; planSaving: boolean; planError: MessageKey | null; planFailed: boolean; onAddItem: () => void;
  declining: string | null; reason: string; onReason: (reason: string) => void; onDecline: (id: string | null) => void; moving: string | null;
  moveConflict: boolean; moveError: MessageKey | null; moveFailed: boolean; onStep: (item: PlanItem, step: keyof typeof stepTarget) => void;
  acceptedBy: string; onAcceptedBy: (name: string) => void; method: AcceptanceMethod; onMethod: (m: AcceptanceMethod) => void;
  accepting: boolean; acceptError: MessageKey | null; acceptFailed: boolean; acceptDone: boolean; onAccept: () => void;
  printing: boolean; printFailed: boolean; onPrint: () => void;
};

/** The dental services starter list load (MDV-P8-010) on Settings › Dental. */
export type StarterView = { loading: boolean; result: { added: number; skipped: number } | null; unsupported: boolean; failed: boolean; onLoad: () => void };
