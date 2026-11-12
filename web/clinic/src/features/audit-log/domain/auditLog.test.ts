import { describe, expect, it } from 'vitest';
import { catalogs, loadCatalog } from '@medvya/i18n';
import { actionKey, all, defaultFilters, exportBody, listQuery, patientRecords, rangeOf } from './auditLog';

describe('action names (MDV-P1-052)', () => {
  it('names a known action in every language and shows an unknown code as it is', async () => {
    await loadCatalog('hi'); await loadCatalog('ar');
    for (const code of ['appointment.walk_in', 'front_desk.board.watch', 'invitation.revoke', 'patient.identifiers.reveal'])
      for (const locale of ['en', 'hi', 'ar'] as const) expect(catalogs[locale]![actionKey(code)!], `${locale} ${code}`).toBeTruthy();
    expect(actionKey('something.new')).toBeNull();
  });
});

describe('access log filters', () => {
  it('defaults to the last 30 days, counted in the clinic time zone', () => {
    const f = defaultFilters(new Date('2026-11-05T12:00:00Z'), 'Asia/Kolkata');
    expect([f.from, f.to, f.zone]).toEqual(['2026-10-06', '2026-11-05', 'Asia/Kolkata']);
    // 20:30 UTC is already the next day in India (02:00 on the 6th), still the same day in London.
    expect(defaultFilters(new Date('2026-11-05T20:30:00Z'), 'Asia/Kolkata').to).toBe('2026-11-06');
    expect(defaultFilters(new Date('2026-11-05T20:30:00Z'), 'Europe/London').to).toBe('2026-11-05');
  });

  it('turns the chosen days into a half-open range in the clinic time zone, whatever the browser zone', () => {
    const day = { ...defaultFilters(), from: '2026-10-01', to: '2026-10-01' };
    expect(rangeOf({ ...day, zone: 'Asia/Kolkata' })).toEqual({ from: '2026-09-30T18:30:00.000Z', to: '2026-10-01T18:30:00.000Z' });
    expect(rangeOf({ ...day, zone: 'Asia/Dubai' })).toEqual({ from: '2026-09-30T20:00:00.000Z', to: '2026-10-01T20:00:00.000Z' });
    // The day daylight saving ends is 25 hours long; the range still runs midnight to midnight.
    expect(rangeOf({ ...defaultFilters(), from: '2026-11-01', to: '2026-11-01', zone: 'America/New_York' }))
      .toEqual({ from: '2026-11-01T04:00:00.000Z', to: '2026-11-02T05:00:00.000Z' });
  });

  it('starts on patient-record events, in the list and the export (MDV audit UX-13)', () => {
    const f = { ...defaultFilters(), from: '', to: '' };
    expect(f.action).toBe(patientRecords);
    expect(listQuery(f, null)).toBe('patientRecords=true');
    expect(exportBody(f)).toMatchObject({ action: null, patientRecords: true });
    expect(exportBody({ ...f, action: all })).toMatchObject({ action: null, patientRecords: false });
  });

  it('sends only the filters that were set, and never a name', () => {
    const base = { ...defaultFilters(), action: all, from: '', to: '' };
    expect(listQuery({ ...base, actor: all }, null)).toBe('');
    expect(listQuery({ ...base, actor: 'u1', action: 'patient.read' }, 'c1')).toBe('actorUserId=u1&action=patient.read&after=c1');
    expect(exportBody({ ...base, patientId: 'p1' })).toMatchObject({ actorUserId: null, action: null, patientId: 'p1' });
  });
});
