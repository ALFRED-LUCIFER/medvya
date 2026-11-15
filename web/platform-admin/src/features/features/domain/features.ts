import { z } from 'zod';
import type { PlatformRole } from '../../session/domain/session';
import { reasonSchema } from '../../team/domain/team';

/** How far Medvya has released a feature in one country (MDV-P5-041); each stage reaches everyone the one before it reached. */
export const stages = ['Off', 'Internal', 'Beta', 'Percentage', 'Everyone'] as const;
export type Stage = (typeof stages)[number];

/** One feature's release in one country, as GET /v1/features answers it. isNew: Medvya has recorded none yet (its starting stage applies). */
export type Release = {
  key: string; clinical: boolean; clinicSwitch: boolean; alwaysOn: boolean; stage: Stage; isNew: boolean; percentage: number;
  organisations: string[]; clinicalSignOff: string | null; changedAt: string | null;
};
export type CountryReleases = { country: string; features: Release[] };

/** Column definitions are data: each is its heading's i18n key, and the first identifies a row (MDV-P2-054). */
export const featureColumns = ['platform.features.col.feature', 'platform.features.col.stage', 'platform.features.col.signOff', 'platform.features.col.changed'] as const;

/** The listed features with a name in words; a key added on the server before its words shows the key itself. */
export const namedKeys = ['clinical.nurse-intake', 'billing.invoices', 'patients.record', 'clinical.allergies', 'prescribing.signing', 'audit.log', 'data.export'] as const;
export const hasName = (key: string): key is (typeof namedKeys)[number] => (namedKeys as readonly string[]).includes(key);

/** Owner and operations change a release; support, finance and reviewer only read (the server answers 403 to them, the buttons are absent). */
export const canChange = (role: PlatformRole) => role === 'Owner' || role === 'Operations';

/** A clinical feature needs a recorded clinical sign-off before it reaches everyone; the server refuses too (clinicalSignOff:required). */
export const needsSignOff = (r: Pick<Release, 'clinical' | 'clinicalSignOff'>, stage: Stage) => r.clinical && stage === 'Everyone' && !r.clinicalSignOff;

/**
 * What the sign-off column says. missing: a clinical feature reaches everyone with no sign-off on record (its starting stage, or a release from
 * before the rule): shown in words, nothing is switched off. The server refuses any new move to everyone without one.
 */
export const signOffState = (r: Pick<Release, 'clinical' | 'alwaysOn' | 'stage' | 'clinicalSignOff'>): 'recorded' | 'missing' | 'none' | 'notNeeded' =>
  r.clinicalSignOff ? 'recorded' : !r.clinical || r.alwaysOn ? 'notNeeded' : r.stage === 'Everyone' ? 'missing' : 'none';

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
/** Organisation ids as pasted from the approvals page: one per line or separated by commas or spaces. */
export const organisationIds = (text: string) => text.split(/[\s,]+/).filter(Boolean);

export const releaseForm = z.object({
  stage: z.enum(stages),
  percentage: z.string(),
  organisations: z.string(),
  clinicalSignOff: z.string().max(500, 'tooLong'),
  reason: reasonSchema,
}).superRefine((f, ctx) => {
  if (f.stage === 'Percentage' && !/^([1-9]|[1-9]\d)$/.test(f.percentage.trim())) ctx.addIssue({ code: 'custom', path: ['percentage'], message: 'percentage' });
  const ids = organisationIds(f.organisations);
  if (ids.some(id => !uuid.test(id))) ctx.addIssue({ code: 'custom', path: ['organisations'], message: 'organisations' });
  else if (f.stage === 'Beta' && ids.length === 0) ctx.addIssue({ code: 'custom', path: ['organisations'], message: 'required' });
});
export type ReleaseForm = z.infer<typeof releaseForm>;

export const formFor = (r: Release): ReleaseForm =>
  ({ stage: r.stage, percentage: r.percentage ? String(r.percentage) : '', organisations: r.organisations.join('\n'), clinicalSignOff: '', reason: '' });

/** The body of POST /v1/features/{country}/{key}. */
export const bodyOf = (f: ReleaseForm) => ({
  stage: f.stage,
  percentage: f.stage === 'Percentage' ? Number(f.percentage.trim()) : null,
  organisations: organisationIds(f.organisations),
  clinicalSignOff: f.clinicalSignOff.trim() || null,
  reason: f.reason,
});

/** What the reason form is for: a new stage for one feature in one country, or turning a feature off in every country. */
export type PendingChange = { kind: 'change'; country: string; release: Release } | { kind: 'off'; release: Release };
