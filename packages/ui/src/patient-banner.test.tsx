// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import axe from 'axe-core';
import { AllergyBlock, PatientBanner, type BannerClinical } from './index';

afterEach(cleanup);

const five: BannerClinical['allergy'] = { state: 'recorded', allergies: [
  { substance: 'Penicillin', reaction: 'rash' }, { substance: 'Sulfonamides' }, { substance: 'Aspirin', reaction: 'wheeze' },
  { substance: 'Latex' }, { substance: 'Peanut', reaction: 'swelling' },
] };
const meera: BannerClinical = { name: 'Meera Iyer', age: 54, sex: 'Female', mrn: 'DQ-10-4482', dob: '12 Mar 1972', allergy: five, consent: 'given', lifeStage: 'Adult', careTeam: 'Dr. Arjun Rao' };
const text = (el: Element) => (el.textContent ?? '').replace(/[⁦-⁩]/g, '');

describe('AllergyBlock (MDV-P1-081)', () => {
  it('shows recorded allergies in words, the first two, and a count that opens the full list', () => {
    render(<AllergyBlock allergy={five} />);
    expect(screen.getByText('Allergy: Penicillin (rash)')).toBeTruthy();
    expect(screen.getByText('Allergy: Sulfonamides')).toBeTruthy();
    expect(screen.queryByText('Allergy: Latex')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: '+3 more' }));
    const dialog = screen.getByRole('dialog', { name: 'Allergies' });
    expect([...dialog.querySelectorAll('[data-slot=allergy]')].map(text)).toEqual([
      'Allergy: Penicillin (rash)', 'Allergy: Sulfonamides', 'Allergy: Aspirin (wheeze)', 'Allergy: Latex', 'Allergy: Peanut (swelling)']);
  });

  it('shows two or fewer allergies with no count', () => {
    render(<AllergyBlock allergy={{ state: 'recorded', allergies: [{ substance: 'Penicillin' }, { substance: 'Latex' }] }} />);
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('keeps not recorded, no known allergies and an empty list apart: an empty list is never "none"', () => {
    const { container, rerender } = render(<AllergyBlock allergy={{ state: 'notRecorded' }} />);
    const notRecorded = container.innerHTML;
    expect(text(container)).toBe('Allergies not recorded');
    expect(container.querySelector('[data-slot=allergy-state]')!.className).toContain('bg-warning-bg');
    rerender(<AllergyBlock allergy={{ state: 'noneKnown' }} />);
    expect(text(container)).toBe('No known allergies');
    expect(container.querySelector('[data-slot=allergy-state]')!.className).toContain('text-neutral-text');
    expect(container.querySelector('svg')).toBeTruthy(); // the tick
    // @ts-expect-error a recorded state needs at least one allergy
    rerender(<AllergyBlock allergy={{ state: 'recorded', allergies: [] }} />);
    expect(container.innerHTML).toBe(notRecorded);
  });
});

describe('PatientBanner (MDV-P1-081)', () => {
  it('desk shows identity only, with a masked MRN, even when clinical props are passed (the safety test)', () => {
    // @ts-expect-error desk takes identity only; a caller that passes clinical data by mistake must still show none of it
    render(<PatientBanner variant="desk" patient={meera} />);
    const banner = screen.getByRole('region', { name: 'Patient Meera Iyer' });
    expect(text(banner)).toBe('Meera Iyer54 years · FemaleMRN •••• 4482Born 12 Mar 1972');
    expect(text(banner)).not.toMatch(/Allerg|Penicillin|consent|Adult|Arjun|DQ-10/i);
  });

  it('full and strip show the clinical fields in their order; strip adds the token and room slots', () => {
    const { container } = render(<>
      <PatientBanner variant="full" patient={meera} />
      <PatientBanner variant="strip" patient={meera} token="A-07" room="Room 3" pinned />
    </>);
    const [full, strip] = [...container.querySelectorAll('[data-slot=patient-banner]')] as HTMLElement[];
    const fields = (el: Element) => [...el.querySelectorAll('[data-field]')].map(e => e.getAttribute('data-field'));
    expect(fields(full)).toEqual(['ageSex', 'mrn', 'dob', 'lifeStage', 'allergy', 'consent', 'careTeam']);
    expect(fields(strip)).toEqual(['ageSex', 'mrn', 'dob', 'allergy', 'consent', 'token', 'room']);
    expect(text(strip)).toContain('MRN DQ-10-4482');
    expect(strip.className.split(' ')).toEqual(expect.arrayContaining(['sticky', 'top-0', 'min-h-14', '[@media(max-height:480px)]:static']));
    expect(full.className).not.toContain('sticky');
  });

  it('has no axe violation', async () => {
    const { container } = render(<main>
      <PatientBanner variant="full" patient={meera} />
      <PatientBanner variant="strip" patient={{ ...meera, name: 'Deepak Nair', allergy: { state: 'notRecorded' }, consent: 'notRecorded' }} />
      <PatientBanner variant="desk" patient={{ name: 'Fatima Al Mansoori', age: 38, sex: 'Female', mrn: 'DQ-AE-0458', dob: '03 Feb 1988' }} />
    </main>);
    expect((await axe.run(container)).violations).toEqual([]);
  });
});
