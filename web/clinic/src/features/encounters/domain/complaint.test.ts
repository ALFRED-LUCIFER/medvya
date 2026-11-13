import { describe, expect, it } from 'vitest';
import { complaintMark, complaintToPrefill, type IntakeComplaint } from './complaint';

const nurse: IntakeComplaint = { text: 'Fever and sore throat for three days (synthetic)', byName: 'Nurse Kavya (synthetic)', at: '2026-11-13T04:30:00Z' };
const who = { byName: nurse.byName, at: nurse.at };

describe('complaintToPrefill (MDV-P1-104)', () => {
  it("fills a blank field with the nurse's words", () => {
    expect(complaintToPrefill(undefined, nurse)).toBe(nurse.text);
    expect(complaintToPrefill('  ', nurse)).toBe(nurse.text);
  });
  it('never replaces a complaint the doctor typed (SAFETY)', () => {
    expect(complaintToPrefill('Cough since Monday (doctor)', nurse)).toBeNull();
  });
  it('guesses nothing without a complaint from the nurse', () => {
    expect(complaintToPrefill(undefined, null)).toBeNull();
    expect(complaintToPrefill(undefined, { ...nurse, text: ' ' })).toBeNull();
  });
});

describe('complaintMark (MDV-P1-104)', () => {
  it('marks the nurse text just put in, before the save returns', () => {
    expect(complaintMark(nurse.text, null, nurse)).toEqual({ kind: 'nurse', ...who });
  });
  it('says edited by the doctor as soon as the text differs', () => {
    expect(complaintMark(`${nurse.text}, worse at night`, null, nurse)).toEqual({ kind: 'nurseEdited', ...who });
    expect(complaintMark(`${nurse.text}, worse`, { source: { source: 'NurseIntake', ...who }, text: nurse.text }, null)).toEqual({ kind: 'nurseEdited', ...who });
  });
  it("follows the server's source once saved", () => {
    expect(complaintMark(nurse.text, { source: { source: 'NurseIntake', ...who }, text: nurse.text }, null)).toEqual({ kind: 'nurse', ...who });
    expect(complaintMark(nurse.text, { source: { source: 'EditedByDoctor', ...who }, text: nurse.text }, null)).toEqual({ kind: 'nurseEdited', ...who });
  });
  it("has no mark on the doctor's own text or a blank field", () => {
    expect(complaintMark('Cough (doctor)', { source: null, text: 'Cough (doctor)' }, null)).toBeNull();
    expect(complaintMark('', null, nurse)).toBeNull();
  });
});
