// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { useForm } from 'react-hook-form';
import { MemoryRouter } from 'react-router';
import { emptyAdd, type AddDraft, type ProblemList, type ProblemRow } from '../domain/problems';
import AssessmentDiagnoses, { type AssessmentDiagnosesProps } from './AssessmentDiagnoses';

afterEach(cleanup);

const row = (over: Partial<ProblemRow>): ProblemRow => ({
  id: 'pr-1', code: 'E11.9', codeSystem: 'ICD-10', codeSystemVersion: '2019', description: 'Type 2 diabetes', verification: 'Verified', status: 'Active',
  onsetOn: null, source: 'Clinician', addedHere: false, confirmed: true, recordedAt: '2026-11-01T10:00:00Z', ...over,
});
const view = (rows: ProblemRow[], locked = false): ProblemList => ({ encounterId: 'e-1', patientId: 'p-1', rows, locked });

function Harness(props: Partial<AssessmentDiagnosesProps>) {
  const addForm = useForm<AddDraft>({ defaultValues: emptyAdd });
  return (
    <MemoryRouter>
      <AssessmentDiagnoses view={view([])} loading={false} problem={null} problemsPath="/consult/e-1/problems" addForm={addForm} search="" results={[]}
        picked={null} adding={false} online onSearch={() => {}} onPick={() => {}} onClearPick={() => {}} onAdd={e => e.preventDefault()} {...props} />
    </MemoryRouter>
  );
}

describe('AssessmentDiagnoses (MDV-P1-094)', () => {
  it('says each active diagnosis and its state in words; resolved ones are left out', () => {
    render(<Harness view={view([
      row({}),
      row({ id: 'pr-2', code: 'I20.9', description: 'Angina', verification: 'Provisional', addedHere: true }),
      row({ id: 'pr-3', code: null, description: 'Knee pain', status: 'Resolved' }),
    ])} />);
    const chips = screen.getByRole('list', { name: 'Diagnoses in this consultation' }).querySelectorAll('li');
    expect([...chips].map(c => c.textContent)).toEqual([
      '⁦E11.9⁩ Type 2 diabetes · existing',
      '⁦I20.9⁩ Angina · new · Provisional · not confirmed',
    ]);
    expect(screen.getByRole('link', { name: 'Open the full problem list' }).getAttribute('href')).toBe('/consult/e-1/problems');
  });

  it('opens the shared add form inline, searches, picks and submits through the controller handlers', () => {
    const onSearch = vi.fn(), onPick = vi.fn(), onAdd = vi.fn((e: { preventDefault: () => void }) => e.preventDefault());
    const code = { code: 'I20.9', title: 'Angina, not specified' };
    render(<Harness search="angina" results={[code]} onSearch={onSearch} onPick={onPick} onAdd={onAdd} />);
    const toggle = screen.getByRole('button', { name: 'Add diagnosis' });
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    fireEvent.click(toggle);
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    const search = screen.getByLabelText('Search ICD-10 by code or words');
    expect(document.activeElement).toBe(search);
    fireEvent.change(search, { target: { value: 'angin' } });
    expect(onSearch).toHaveBeenCalledWith('angin');
    fireEvent.click(screen.getByRole('button', { name: /I20\.9/ }));
    expect(onPick).toHaveBeenCalledWith(code);
    fireEvent.click(screen.getByRole('button', { name: 'Add problem' }));
    expect(onAdd).toHaveBeenCalledTimes(1);
  });

  it('Escape closes the add area and returns focus to Add diagnosis', () => {
    render(<Harness />);
    const toggle = screen.getByRole('button', { name: 'Add diagnosis' });
    fireEvent.click(toggle);
    fireEvent.keyDown(screen.getByLabelText('Search ICD-10 by code or words'), { key: 'Escape' });
    expect(screen.queryByLabelText('Search ICD-10 by code or words')).toBeNull();
    expect(document.activeElement).toBe(toggle);
  });

  it('offline, the add button of the form is disabled; an ended consultation offers no add', () => {
    const { unmount } = render(<Harness online={false} />);
    fireEvent.click(screen.getByRole('button', { name: 'Add diagnosis' }));
    expect((screen.getByRole('button', { name: 'Add problem' }) as HTMLButtonElement).disabled).toBe(true);
    unmount();
    render(<Harness view={view([row({})], true)} />);
    expect(screen.queryByRole('button', { name: 'Add diagnosis' })).toBeNull();
  });

  it('shows the server refusal in words', () => {
    render(<Harness problem="duplicate" />);
    expect(screen.getByRole('alert').textContent).toBe('This code is already an active problem. Edit that one instead.');
  });
});
