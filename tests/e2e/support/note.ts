import type { Page, Route } from '@playwright/test';

// The consultation note and its template catalogue as the server sends them (MDV-P2-009, MDV-P2-025), for specs that mock the API.
const soap = (...extra: string[]) => ['chiefComplaint', 'subjective', ...extra, 'objective', 'assessment', 'plan']
  .map(id => ({ id, required: ['chiefComplaint', 'assessment', 'plan'].includes(id) }));
const template = (key: string, ...extra: string[]) => ({ key, version: 1, sections: soap(...extra), quickPhrases: [] });
// MDV-P2-042: version 2 adds the review of systems after the history and the examination by system before Objective (NoteTemplates.Structured).
const structured = (key: string, symptoms: string[], systems: string[], ...extra: string[]) => {
  const [cc, subjective, ...rest] = soap(...extra);
  const objective = rest.length - 3;
  return { key, version: 2, quickPhrases: [], sections: [cc, subjective, { id: 'reviewOfSystems', required: false, kind: 'ReviewOfSystems', items: symptoms }, ...rest.slice(0, objective),
    { id: 'examination', required: false, kind: 'Examination', items: systems }, ...rest.slice(objective)] };
};
export const catalog = {
  templates: [{ ...template('general-opd'), quickPhrases: ['noRedFlags', 'reviewIfWorse', 'adviceFluidsRest'] }, structured('diabetes-review', ['tiredness', 'visionChange', 'chestPain', 'breathless', 'abdominalPain', 'nightUrination', 'tinglingFeet', 'footSores'],
    ['general', 'heart', 'lungs', 'abdomen', 'feet', 'nervousSystem'], 'selfMonitoring', 'footAndEyeCare'), template('hypertension', 'homeBloodPressure'),
    template('paediatrics', 'growthAndDevelopment', 'immunisations'), template('antenatal', 'obstetricHistory', 'fetalWellbeing'), template('teleconsult', 'remoteContext'),
    structured('older-adult-review', ['weightLoss', 'dizzyOnStanding', 'breathless', 'indigestion', 'nightUrination', 'kneePain', 'weakness', 'lowMood'],
      ['general', 'heart', 'lungs', 'joints', 'nervousSystem', 'feet'], 'standingBloodPressure', 'walkingTest', 'frailty', 'homeSafety')],
  mine: ['general-opd', 'diabetes-review'],
};

type Addendum = { versionNo: number; amendsVersionNo: number; reason: string; text: string; signedBy: string; signedAt: string; contentHash: string; signerName?: string };
type Note = { templateKey: string; sections: Record<string, string>; version: number; status: string; signature?: { versionNo: number; signedBy: string; signedAt: string; contentHash: string; signerName?: string; signerLicence?: string }; addenda?: Addendum[]; signAsName?: string;
  /** MDV-P2-044: the doctor's record of the recording consent (null: not recorded) and the sections with an undecided AI draft. */
  recordingConsent?: 'Agreed' | 'Declined' | 'Withdrawn' | null; undecidedAi?: string[];
  /** MDV-P2-042: the template the server suggests for a note with nothing saved. */
  suggestedTemplateKey?: string | null;
  /** MDV-P1-104: where the chief complaint came from, as the server decides it. */
  chiefComplaintSource?: ComplaintSource | null;
  /** MDV-P2-049: the visit record kept with the signature, and the encounter's facility (signed notes). */
  atSigning?: NoteRecord | null; facilityName?: string | null };
type NoteRecord = {
  vitals: { code: string; value: number; unit: string; takenAt: string; source: string; recordedByName: string | null; flag: string | null }[];
  allergies: { state: 'NotRecorded' | 'NoKnownAllergies' | 'Listed'; allergies: { substance: string; reaction: string | null; status: string; source: string }[] };
  diagnoses: { icd10Code: string | null; description: string; verification: string; onsetOn: string | null; source: string; status: string }[];
  medicines: { name: string; dose: string | null; prescriptionId: string }[];
};
type ComplaintSource = { source: 'NurseIntake' | 'EditedByDoctor'; byName: string | null; at: string | null };
// The doctor profile's name, which the server uses for the signature, the addenda and the sign button; the session's display name differs on purpose.
const signer = 'Dr Asha Raman (synthetic)';
export type NoteMock = {
  note: Note; puts: { templateKey: string; manual: boolean; recordingConsent: Note['recordingConsent']; baseVersion: number; sections: Record<string, string> }[]; chosen: string[][];
  /** The next PUT answers like this instead of saving: a 409 stale_note, or a dropped connection. */
  next: 'stale' | 'offline' | null;
  /** MDV-P1-089: each PUT answers this late (a slow link), so a spec can look at the page while a save is in flight. */
  saveDelayMs: number;
  /** Sign calls received, the re-authentications asked for, and the answer to the next sign instead of the real one (the server's rules are ConsultNoteTests). */
  signs: { baseVersion: number }[]; reauths: { returnUrl?: string }[]; reauthed: boolean;
  signNext: 'stale' | 'denied' | 'blank' | 'unreviewed' | null;
  /** The visit's medication reconciliation as the checklist reads it (MDV-P2-044): nothing to reconcile unless a spec says so. */
  reconciliation: { confirmedAt: string | null; total: number };
  /** Addenda received (MDV-P2-013); they need the same re-authentication as signing. PDFs asked for, and the answer to the next one. */
  addendumPosts: { text: string; reason: string }[]; pdfs: number; pdfNext: 'unsupported' | null;
  /** What the server offers to copy into the note (MDV-P2-009): verified vitals of this visit and earlier signed notes. Empty until a spec seeds them. */
  vitals: { code: string; value: number; unit: string; takenAt: string; source: string; recordedByName: string | null }[];
  previous: { encounterId: string; signedAt: string; signedByName: string | null; templateKey: string; sections: Record<string, string> }[];
  /** MDV-P1-104: the chief complaint the nurse saved at this visit's intake (null: none, as with nurse intake off). */
  chiefComplaint: { text: string; byName: string | null; at: string | null } | null;
  /** The visit's problem list as the checklist reads it (MDV audit UX-11): one active diagnosis unless a spec clears it. */
  problems: { id: string; code: string | null; description: string; verification: string; status: 'Active' | 'Resolved'; addedHere: boolean; confirmed: boolean }[];
  /** The visit's vitals and allergies entry in the consultation (MDV audit P0-2): nothing recorded until the doctor confirms "no known allergies". */
  noKnownAllergies: boolean;
  /** MDV-P1-091: when the doctor pressed "Confirmed with the patient" (the encounter's stored confirmation); confirmed unless a spec clears it. */
  identityConfirmedAt: string | null; identityConfirms: number;
  /** MDV-P2-049: what a sign answers as the record at signing; null: built from this mock's vitals, allergy answer and problems (the server's rule is NotePrintRecordTests). */
  atSigning: NoteRecord | null;
};

/** The encounter as the server sends it in the banner, with the stored identity confirmation of the mock (MDV-P1-091). */
export const encounterOf = (mock: Pick<NoteMock, 'identityConfirmedAt'> | null) => ({
  id: 'e-1', appointmentId: 'a1', patientId: 'p-a1', facilityId: 'f-1', leadClinicianId: 'u-rao', status: 'Active', authorityMode: 'Medvya',
  startedAt: '2026-10-19T04:35:00Z', endedAt: null, room: 'Room 3', identityConfirmedAt: mock?.identityConfirmedAt ?? null, identityConfirmedBy: mock?.identityConfirmedAt ? 'u-rao' : null,
});

export async function mockNote(page: Page, seed: Partial<Note> = {}): Promise<NoteMock> {
  const mock: NoteMock = { note: { templateKey: 'general-opd', sections: {}, version: 0, status: 'Draft', signAsName: signer, recordingConsent: null, undecidedAi: [], ...seed }, puts: [], chosen: [], next: null, saveDelayMs: 0, signs: [], reauths: [], reauthed: false, signNext: null, reconciliation: { confirmedAt: null, total: 0 }, addendumPosts: [], pdfs: 0, pdfNext: null, vitals: [], previous: [], chiefComplaint: null, noKnownAllergies: false, identityConfirmedAt: '2026-10-19T04:36:00Z', identityConfirms: 0, atSigning: null,
    problems: [{ id: 'pr-1', code: 'E11.9', description: 'Type 2 diabetes (synthetic)', verification: 'Verified', status: 'Active', addedHere: false, confirmed: true }] };
  await page.route('**/v1/encounters/e-1/medicines/reconciliation', r => r.fulfill({ json: { encounterId: 'e-1', patientId: 'p-a1', rows: [], decided: 0, disagree: 0, ...mock.reconciliation } }));
  await page.route('**/v1/encounters/e-1/problems', r => r.fulfill({ json: { encounterId: 'e-1', patientId: 'p-a1', rows: mock.problems, locked: false } }));
  await page.route('**/v1/encounters/e-1/note/prefill', r => r.fulfill({ json: { vitals: mock.vitals, chiefComplaint: mock.chiefComplaint } }));
  await page.route('**/v1/encounters/e-1/note/previous', r => r.fulfill({ json: { notes: mock.previous } }));
  const intake = () => ({ appointmentId: 'a1', patientId: 'p-a1', facilityId: 'f-1', state: 'InConsult', chiefComplaint: null, vitals: [], allergies: [], medicines: [],
    noKnownAllergiesConfirmed: mock.noKnownAllergies, escalated: false, handedOff: false, blockers: [] });
  await page.route('**/v1/intake/a1', r => r.fulfill({ json: { intake: intake(), timeZone: 'Asia/Kolkata',
    patient: { id: 'p-a1', fullName: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12', ageYears: 54, sexAtBirth: 'Female' } } }));
  await page.route('**/v1/intake/a1/no-known-allergies', r => { mock.noKnownAllergies = true; return r.fulfill({ json: intake() }); });
  await page.route('**/v1/encounters/e-1/identity-confirmation', r => {
    mock.identityConfirms++;
    mock.identityConfirmedAt ??= '2026-10-19T04:37:00Z';
    return r.fulfill({ json: { ...encounterOf(mock), room: 'Room 3' } });
  });
  const view = () => ({ encounterId: 'e-1', templateVersion: 1, manual: true, updatedAt: null, ...mock.note });
  await page.route('**/v1/encounters/e-1/note', async r => {
    if (r.request().method() === 'GET') return r.fulfill({ json: view() });
    const body = r.request().postDataJSON() as NoteMock['puts'][number];
    if (mock.saveDelayMs) await new Promise(done => setTimeout(done, mock.saveDelayMs));
    if (mock.next === 'offline') { mock.next = null; return r.abort('connectionfailed'); }
    if (mock.next === 'stale') { mock.next = null; return r.fulfill({ status: 409, contentType: 'application/problem+json', json: { code: 'stale_note', currentVersion: mock.note.version } }); }
    mock.puts.push(body);
    // The server's rule (ComplaintSourceRule, tested in ComplaintSourceRuleTests): from intake only into a blank field, edited once changed.
    const [before, text, source] = [mock.note.sections.chiefComplaint, body.sections.chiefComplaint, mock.note.chiefComplaintSource ?? null];
    const chiefComplaintSource: ComplaintSource | null = !text?.trim() ? null : text === before ? source
      : !before?.trim() && mock.chiefComplaint && text === mock.chiefComplaint.text ? { source: 'NurseIntake', byName: mock.chiefComplaint.byName, at: mock.chiefComplaint.at }
      : source && { ...source, source: 'EditedByDoctor' };
    mock.note = { ...mock.note, templateKey: body.templateKey, sections: body.sections, recordingConsent: body.recordingConsent, version: body.baseVersion + 1, chiefComplaintSource };
    return r.fulfill({ json: view() });
  });
  const problem = (r: Route, status: number, json: object) => r.fulfill({ status, contentType: 'application/problem+json', json });
  await page.route('**/v1/encounters/e-1/note/sign', r => {
    mock.signs.push(r.request().postDataJSON() as { baseVersion: number });
    const next = mock.signNext;
    mock.signNext = null;
    if (!mock.reauthed) return problem(r, 403, { code: 'reauth_required' });
    if (!mock.identityConfirmedAt) return problem(r, 409, { code: 'identity_not_confirmed' }); // the server's rule (MDV-P1-109, SignNoteTests)
    if (next === 'denied') return problem(r, 403, { code: 'no_care_relationship' });
    if (next === 'stale') return problem(r, 409, { code: 'stale_note', currentVersion: mock.note.version + 1 });
    if (next === 'blank') return problem(r, 400, { errors: { 'sections.plan': ['required'], 'sections.chiefComplaint': ['required'] } });
    if (next === 'unreviewed') return problem(r, 400, { errors: { undecidedAi: ['undecided'] } });
    mock.note = { ...mock.note, status: 'Signed', version: mock.note.version + 1,
      signature: { versionNo: 1, signedBy: 'u-rao', signedAt: '2026-10-19T04:40:00Z', contentHash: 'a'.repeat(64), signerName: signer, signerLicence: 'Tamil Nadu Medical Council 84213' },
      facilityName: 'Iyer Family Clinic · Adyar',
      atSigning: mock.atSigning ?? { vitals: mock.vitals.map(v => ({ flag: null, ...v })), allergies: { state: mock.noKnownAllergies ? 'NoKnownAllergies' : 'NotRecorded', allergies: [] },
        diagnoses: mock.problems.map(p => ({ icd10Code: p.code, description: p.description, verification: p.verification, onsetOn: null, source: 'ClinicianVerified', status: p.status })), medicines: [] } };
    return r.fulfill({ json: view() });
  });
  await page.route('**/v1/encounters/e-1/note/addenda', r => {
    const body = r.request().postDataJSON() as { text: string; reason: string };
    mock.addendumPosts.push(body);
    if (!mock.reauthed) return problem(r, 403, { code: 'reauth_required' });
    const addenda = mock.note.addenda ?? [];
    const versionNo = addenda.length + 2;
    mock.note = { ...mock.note, addenda: [...addenda, { versionNo, amendsVersionNo: versionNo - 1, ...body, signedBy: 'u-rao', signedAt: '2026-10-19T05:00:00Z', contentHash: 'c'.repeat(64), signerName: signer }] };
    return r.fulfill({ json: view() });
  });
  await page.route('**/v1/encounters/e-1/note/pdf', r => {
    mock.pdfs++;
    if (mock.pdfNext === 'unsupported') { mock.pdfNext = null; return problem(r, 422, { code: 'pdf_unsupported_text' }); }
    return r.fulfill({ contentType: 'application/pdf', headers: { 'X-Content-SHA256': 'd'.repeat(64) }, body: '%PDF-1.4\n%%EOF\n' });
  });
  // The provider's page is a stub: a spec "returns" from it by opening the consult path again, as the BFF's redirect does.
  await page.route('**/bff/reauth', r => { mock.reauths.push(r.request().postDataJSON() as { returnUrl?: string }); mock.reauthed = true; return r.fulfill({ json: { location: '/idp-stub' } }); });
  await page.route('**/idp-stub', r => r.fulfill({ contentType: 'text/html', body: '<title>Provider</title><p>Confirm your identity</p>' }));
  await page.route('**/v1/note-templates', r => r.fulfill({ json: { ...catalog, mine: mock.chosen.at(-1) ?? catalog.mine } }));
  await page.route('**/v1/note-templates/mine', r => { const keys = (r.request().postDataJSON() as { keys: string[] }).keys; mock.chosen.push(keys); return r.fulfill({ json: keys }); });
  return mock;
}
