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
  /** The procedure consent the item needs before it starts (MDV-P8-019), and the latest one signed. */
  consentKey: string | null; consent: Consent | null;
};
/** A signed procedure consent. current: on the current form version and not withdrawn, so the item may start. */
export type Consent = {
  id: string; planItemId: string; templateKey: string; templateVersion: number; language: string; signedBy: string; signedAt: string;
  withdrawnAt: string | null; withdrawalReason: string | null; current: boolean;
};
/** The consent forms (DentalConsentRules on the server); the wording is in the catalogue (dentalConsent.<key>.*). */
export const consentKeys = ['extraction', 'rootCanal', 'implant', 'sedation'] as const;
export type ConsentKey = (typeof consentKeys)[number];
/** Whether the item waits for a consent before it can start or finish. */
export const needsConsent = (item: Pick<PlanItem, 'consentKey' | 'consent'>) => !!item.consentKey && !item.consent?.current;
export type Acceptance = { id: string; acceptedBy: string; method: AcceptanceMethod; totalMinor: number; currency: string; acceptedAt: string; recordedBy: string };
export type DentalPlan = { items: PlanItem[]; totalMinor: number; currency: string | null; acceptance: Acceptance | null; acceptanceCurrent: boolean };
/** The medical history questions asked before dental treatment (MDV-P8-014), in the order they are asked (DentalHistoryRules.Questions on the server). */
export const historyQuestions = ['BloodThinners', 'Diabetes', 'Pregnancy', 'HeartOrEndocarditisRisk', 'Bisphosphonates', 'LocalAnaestheticAllergy'] as const;
export type HistoryQuestion = (typeof historyQuestions)[number];
/** A confirmed history check: the questions answered yes, on which visit, by whom and when. */
export type HistoryCheck = { id: string; encounterId: string; yes: HistoryQuestion[]; note: string | null; confirmedBy: string; confirmedAt: string; author: string | null };

export type DentalChart = {
  teeth: ToothFinding[]; history: ToothFinding[]; plan: DentalPlan; openEncounterId: string | null; timeZone: string; rulesSignedOff: boolean;
  historyCheck: HistoryCheck | null;
};

/** Whether the history was confirmed on the doctor's open visit: treatment starts only then (the server refuses otherwise, "historyCheck:required"). */
export const historyOnVisit = (c: Pick<DentalChart, 'historyCheck' | 'openEncounterId'>) => !!c.openEncounterId && c.historyCheck?.encounterId === c.openEncounterId;

/** Every question answered, yes or no. */
export const historyComplete = (answers: Partial<Record<HistoryQuestion, boolean>>) => historyQuestions.every(q => answers[q] !== undefined);
export type DentalService = { id: string; name: string; code: string | null; durationMinutes: number | null; priceMinor: number | null; currency: string };

/** Caries and fillings are recorded on surfaces; every other condition is of the whole tooth (DentalRules.SurfaceConditions on the server). */
export const surfaceConditions: ReadonlySet<ToothCondition> = new Set(['Caries', 'Filling']);
export const surfaceOrder = ['M', 'D', 'O', 'B', 'L'] as const;
export type Surface = (typeof surfaceOrder)[number];

/** The library's tooth chart vocabulary (ToothChart in @medvya/ui), restated here because the domain is pure. */
export type Dentition = 'permanent' | 'primary' | 'mixed';
export type ConditionTone = 'danger' | 'warning' | 'success' | 'info' | 'brand' | 'neutral';
export type ToothFill = 'hatch' | 'solid' | 'dots' | 'faded';
export type ToothSymbol = 'line' | 'cross' | 'screw' | 'bar' | 'arrows' | 'zigzag';
export type ToothMark = { abbr: string; label: string; tone: ConditionTone; surfaces?: string; fill?: ToothFill; symbol?: ToothSymbol };

/** The tone each condition draws in; the code on the tooth carries the meaning, the tone only backs it up. */
export const conditionTone: Record<ToothCondition, ConditionTone> = {
  Sound: 'success', Caries: 'danger', Fractured: 'danger', Mobile: 'warning', Missing: 'neutral',
  Filling: 'info', Crown: 'warning', RootCanalTreated: 'info', Implant: 'brand', Bridge: 'warning',
};

/** How each condition is drawn on the tooth (Medvya Dental DN-02): caries hatched and fillings solid on their surfaces, a crown dotted, a missing or implant
 * tooth faded, and the symbol on top. */
export const conditionDrawing: Record<ToothCondition, { fill?: ToothFill; symbol?: ToothSymbol }> = {
  Sound: {}, Caries: { fill: 'hatch' }, Filling: { fill: 'solid' }, Crown: { fill: 'dots' }, RootCanalTreated: { symbol: 'line' },
  Missing: { fill: 'faded', symbol: 'cross' }, Implant: { fill: 'faded', symbol: 'screw' }, Bridge: { symbol: 'bar' }, Mobile: { symbol: 'arrows' }, Fractured: { symbol: 'zigzag' },
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
    ...(f.surfaces ? { surfaces: f.surfaces } : {}),
    ...conditionDrawing[f.condition],
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
  'historyCheck:required', 'consent:required', 'consent:not_needed', 'consent:withdrawn', 'language:invalid', 'signedBy:required', 'signedBy:too_long', 'signature:invalid',
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
  answers: Partial<Record<HistoryQuestion, boolean>>; onAnswer: (q: HistoryQuestion, yes: boolean) => void; historyNote: string; onHistoryNote: (note: string) => void;
  historyOnVisit: boolean; historySaving: boolean; historyFailed: boolean; historyIncomplete: boolean; historyDone: boolean; onConfirmHistory: () => void;
  consenting: string | null; onConsenting: (itemId: string | null) => void; consentSigner: string; onConsentSigner: (name: string) => void;
  onSignature: (png: string | null) => void; hasSignature: boolean; consentSaving: boolean; consentError: MessageKey | null; consentFailed: boolean; onSignConsent: () => void;
  withdrawing: string | null; onWithdrawing: (consentId: string | null) => void; withdrawReason: string; onWithdrawReason: (reason: string) => void;
  withdrawSaving: boolean; withdrawFailed: boolean; onWithdraw: () => void; onConsentPdf: (consentId: string) => void; consentPdfFailed: boolean;
};

/** Settings › Dental, the clinic's note template (MDV-P8-015): on means new notes start on the dental template. */
export type DentalTemplateView = { loading: boolean; loadFailed: boolean; on: boolean; saving: boolean; saveFailed: boolean; onChange: (on: boolean) => void };

/** The clinic default the switch saves: the dental template only, or General OPD. */
export const dentalTemplateDefault = (on: boolean): string[] => (on ? ['dental'] : ['general-opd']);

/** The dental services starter list load (MDV-P8-010) on Settings › Dental. */
export type StarterView = { loading: boolean; result: { added: number; skipped: number } | null; unsupported: boolean; failed: boolean; onLoad: () => void };
