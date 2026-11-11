import type { Page, Route } from '@playwright/test';

// The consultation note and its template catalogue as the server sends them (MDV-P2-009, MDV-P2-025), for specs that mock the API.
const soap = (...extra: string[]) => ['chiefComplaint', 'subjective', ...extra, 'objective', 'assessment', 'plan']
  .map(id => ({ id, required: ['chiefComplaint', 'assessment', 'plan'].includes(id) }));
const template = (key: string, ...extra: string[]) => ({ key, version: 1, sections: soap(...extra), quickPhrases: [] });
export const catalog = {
  templates: [{ ...template('general-opd'), quickPhrases: ['noRedFlags', 'reviewIfWorse', 'adviceFluidsRest'] }, template('diabetes-review', 'selfMonitoring', 'footAndEyeCare'), template('hypertension', 'homeBloodPressure'),
    template('paediatrics', 'growthAndDevelopment', 'immunisations'), template('antenatal', 'obstetricHistory', 'fetalWellbeing'), template('teleconsult', 'remoteContext')],
  mine: ['general-opd', 'diabetes-review'],
};

type Addendum = { versionNo: number; amendsVersionNo: number; reason: string; text: string; signedBy: string; signedAt: string; contentHash: string };
type Note = { templateKey: string; sections: Record<string, string>; version: number; status: string; signature?: { versionNo: number; signedBy: string; signedAt: string; contentHash: string }; addenda?: Addendum[] };
export type NoteMock = {
  note: Note; puts: { templateKey: string; manual: boolean; baseVersion: number; sections: Record<string, string> }[]; chosen: string[][];
  /** The next PUT answers like this instead of saving: a 409 stale_note, or a dropped connection. */
  next: 'stale' | 'offline' | null;
  /** Sign calls received, the re-authentications asked for, and the answer to the next sign instead of the real one (the server's rules are ConsultNoteTests). */
  signs: { baseVersion: number }[]; reauths: { returnUrl?: string }[]; reauthed: boolean;
  signNext: 'stale' | 'denied' | 'blank' | null;
  /** Addenda received (MDV-P2-013); they need the same re-authentication as signing. PDFs asked for, and the answer to the next one. */
  addendumPosts: { text: string; reason: string }[]; pdfs: number; pdfNext: 'unsupported' | null;
  /** What the server offers to copy into the note (MDV-P2-009): verified vitals of this visit and earlier signed notes. Empty until a spec seeds them. */
  vitals: { code: string; value: number; unit: string; takenAt: string; source: string; recordedByName: string | null }[];
  previous: { encounterId: string; signedAt: string; signedByName: string | null; templateKey: string; sections: Record<string, string> }[];
};

export async function mockNote(page: Page, seed: Partial<Note> = {}): Promise<NoteMock> {
  const mock: NoteMock = { note: { templateKey: 'general-opd', sections: {}, version: 0, status: 'Draft', ...seed }, puts: [], chosen: [], next: null, signs: [], reauths: [], reauthed: false, signNext: null, addendumPosts: [], pdfs: 0, pdfNext: null, vitals: [], previous: [] };
  await page.route('**/v1/encounters/e-1/note/prefill', r => r.fulfill({ json: { vitals: mock.vitals } }));
  await page.route('**/v1/encounters/e-1/note/previous', r => r.fulfill({ json: { notes: mock.previous } }));
  const view = () => ({ encounterId: 'e-1', templateVersion: 1, manual: true, updatedAt: null, ...mock.note });
  await page.route('**/v1/encounters/e-1/note', async r => {
    if (r.request().method() === 'GET') return r.fulfill({ json: view() });
    const body = r.request().postDataJSON() as NoteMock['puts'][number];
    if (mock.next === 'offline') { mock.next = null; return r.abort('connectionfailed'); }
    if (mock.next === 'stale') { mock.next = null; return r.fulfill({ status: 409, contentType: 'application/problem+json', json: { code: 'stale_note', currentVersion: mock.note.version } }); }
    mock.puts.push(body);
    mock.note = { ...mock.note, templateKey: body.templateKey, sections: body.sections, version: body.baseVersion + 1 };
    return r.fulfill({ json: view() });
  });
  const problem = (r: Route, status: number, json: object) => r.fulfill({ status, contentType: 'application/problem+json', json });
  await page.route('**/v1/encounters/e-1/note/sign', r => {
    mock.signs.push(r.request().postDataJSON() as { baseVersion: number });
    const next = mock.signNext;
    mock.signNext = null;
    if (!mock.reauthed) return problem(r, 403, { code: 'reauth_required' });
    if (next === 'denied') return problem(r, 403, { code: 'no_care_relationship' });
    if (next === 'stale') return problem(r, 409, { code: 'stale_note', currentVersion: mock.note.version + 1 });
    if (next === 'blank') return problem(r, 400, { errors: { 'sections.plan': ['required'], 'sections.chiefComplaint': ['required'] } });
    mock.note = { ...mock.note, status: 'Signed', version: mock.note.version + 1,
      signature: { versionNo: 1, signedBy: 'u-rao', signedAt: '2026-10-19T04:40:00Z', contentHash: 'a'.repeat(64) } };
    return r.fulfill({ json: view() });
  });
  await page.route('**/v1/encounters/e-1/note/addenda', r => {
    const body = r.request().postDataJSON() as { text: string; reason: string };
    mock.addendumPosts.push(body);
    if (!mock.reauthed) return problem(r, 403, { code: 'reauth_required' });
    const addenda = mock.note.addenda ?? [];
    const versionNo = addenda.length + 2;
    mock.note = { ...mock.note, addenda: [...addenda, { versionNo, amendsVersionNo: versionNo - 1, ...body, signedBy: 'u-rao', signedAt: '2026-10-19T05:00:00Z', contentHash: 'c'.repeat(64) }] };
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
