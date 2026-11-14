// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import type { SignPatient } from '../domain/note';
import { SignNote } from './NoteSign';

afterEach(cleanup);

// Synthetic cast (docs/00-overview/personas.md): Meera Iyer.
const patient: SignPatient = {
  name: 'Meera Iyer (synthetic)', mrn: '000001', dateOfBirth: '1972-03-12',
  allergies: { state: 'Listed', allergies: [{ substance: 'Penicillin', reaction: 'Rash', status: 'Confirmed', source: 'ClinicianVerified' }] },
};
const sign = (p: SignPatient) => render(<SignNote canSign signing={false} patient={p} doctorName="Asha Raman (synthetic)" confirming problem={null} remaining={0}
  onRequest={() => {}} onCancel={() => {}} onConfirm={() => {}} resume={{ focus: () => {}, confirmed: false }} />);

describe('SignNote dialog (MDV-P2-060 AC4)', () => {
  it('repeats the allergy as the library AllergyBlock', () => {
    sign(patient);
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText('Allergy: Penicillin (Verified)').closest('[data-slot=allergy]')).toBeTruthy();
  });

  it('keeps not recorded and no known allergies apart', () => {
    const { unmount } = sign({ ...patient, allergies: { state: 'NotRecorded', allergies: [] } });
    expect(within(screen.getByRole('dialog')).getByText('Allergies not recorded').closest('[data-slot=allergy-state]')).toBeTruthy();
    unmount();
    sign({ ...patient, allergies: { state: 'NoKnownAllergies', allergies: [] } });
    expect(within(screen.getByRole('dialog')).getByText('No known allergies').closest('[data-slot=allergy-state]')).toBeTruthy();
  });
});
