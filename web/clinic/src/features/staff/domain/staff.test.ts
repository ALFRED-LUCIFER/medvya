import { describe, expect, it } from 'vitest';
import { addableRows, permissionRows, type RoleCatalogue } from './staff';

const catalogue: RoleCatalogue = {
  builtIn: [
    { role: 'Receptionist', rows: [{ capability: 'ClinicalNotes', access: 'None', extra: null }, { capability: 'AuditLog', access: 'None', extra: 'Read' }, { capability: 'CheckInQueue', access: 'Full', extra: null }] },
    { role: 'Nurse', rows: [] }, { role: 'CareCoordinator', rows: [] }, { role: 'PracticeAdmin', rows: [] },
  ],
  custom: [{ id: 'c1', name: 'Desk with audit', baseRole: 'Receptionist', capabilities: ['AuditLog'] }],
};

describe('permissionRows', () => {
  it('shows a built-in role as the matrix column', () => {
    expect(permissionRows(catalogue, { kind: 'builtIn', role: 'Receptionist' }).map(r => [r.capability, r.access, r.added])).toEqual([
      ['ClinicalNotes', 'None', false], ['AuditLog', 'None', false], ['CheckInQueue', 'Full', false],
    ]);
  });

  it('adds a custom role extras at the level the server offers, never clinical rows', () => {
    const rows = permissionRows(catalogue, { kind: 'custom', id: 'c1' });
    expect(rows.find(r => r.capability === 'AuditLog')).toEqual({ capability: 'AuditLog', access: 'Read', added: true });
    expect(rows.find(r => r.capability === 'ClinicalNotes')?.access).toBe('None');
  });

  it('has no rows for Doctor: nothing is created here', () => {
    expect(permissionRows(catalogue, { kind: 'doctor' })).toEqual([]);
  });
});

describe('addableRows', () => {
  it('lists only rows the server marked addable', () => {
    expect(addableRows(catalogue, 'Receptionist').map(r => r.capability)).toEqual(['AuditLog']);
  });
});
