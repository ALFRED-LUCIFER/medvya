import type { Page } from '@playwright/test';

// The consultation note and its template catalogue as the server sends them (MDV-P2-009, MDV-P2-025), for specs that mock the API.
const soap = (...extra: string[]) => ['chiefComplaint', 'subjective', ...extra, 'objective', 'assessment', 'plan']
  .map(id => ({ id, required: ['chiefComplaint', 'assessment', 'plan'].includes(id) }));
const template = (key: string, ...extra: string[]) => ({ key, version: 1, sections: soap(...extra), quickPhrases: [] });
export const catalog = {
  templates: [template('general-opd'), template('diabetes-review', 'selfMonitoring', 'footAndEyeCare'), template('hypertension', 'homeBloodPressure'),
    template('paediatrics', 'growthAndDevelopment', 'immunisations'), template('antenatal', 'obstetricHistory', 'fetalWellbeing'), template('teleconsult', 'remoteContext')],
  mine: ['general-opd', 'diabetes-review'],
};

type Note = { templateKey: string; sections: Record<string, string>; version: number; status: string };
export type NoteMock = {
  note: Note; puts: { templateKey: string; manual: boolean; baseVersion: number; sections: Record<string, string> }[]; chosen: string[][];
  /** The next PUT answers like this instead of saving: a 409 stale_note, or a dropped connection. */
  next: 'stale' | 'offline' | null;
};

export async function mockNote(page: Page, seed: Partial<Note> = {}): Promise<NoteMock> {
  const mock: NoteMock = { note: { templateKey: 'general-opd', sections: {}, version: 0, status: 'Draft', ...seed }, puts: [], chosen: [], next: null };
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
  await page.route('**/v1/note-templates', r => r.fulfill({ json: { ...catalog, mine: mock.chosen.at(-1) ?? catalog.mine } }));
  await page.route('**/v1/note-templates/mine', r => { const keys = (r.request().postDataJSON() as { keys: string[] }).keys; mock.chosen.push(keys); return r.fulfill({ json: keys }); });
  return mock;
}
