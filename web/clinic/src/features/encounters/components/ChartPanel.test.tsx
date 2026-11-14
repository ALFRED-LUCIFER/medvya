// @vitest-environment jsdom
import { useState } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import type { Patient360 } from '../../patient360/domain/chart';
import type { ChecklistItem } from '../domain/checklist';
import type { StudioPanel } from '../domain/encounter';
import ChartPanel, { ChartDrawerButton, type ChartPanelProps } from './ChartPanel';

afterEach(cleanup);

// Synthetic cast (docs/00-overview/personas.md): Meera Iyer.
const chart: Patient360['chart'] = {
  allergyState: 'Listed', allergies: [{ substance: 'Penicillin', reaction: 'Rash', status: 'PatientReported', source: 'PatientReported' }],
  problems: [
    { icd10Code: 'E11.9', description: 'Type 2 diabetes', verification: 'Verified', onsetOn: null, source: 'ClinicianVerified', status: 'Active' },
    { icd10Code: null, description: 'Old sprain', verification: 'Verified', onsetOn: null, source: 'ClinicianVerified', status: 'Resolved' },
  ],
  medicines: [
    { name: 'Metformin', dose: '500 mg', state: 'Active', source: 'ClinicianVerified', stoppedAt: null },
    { name: 'Amoxicillin', dose: null, state: 'Stopped', source: 'ClinicianVerified', stoppedAt: '2026-08-01T00:00:00Z' },
  ],
  lastVitals: [], trends: [],
  recentEncounters: [{ id: 'e1', startedAt: '2026-08-14T05:00:00Z', status: 'Ended' }, { id: 'e2', startedAt: '2026-11-14T05:00:00Z', status: 'Active' }],
  openEncounterId: 'e2', startableAppointmentId: null,
  lastLabs: [
    { code: 'HbA1c', value: 7.9, unit: '%', takenAt: '2026-08-14T05:00:00Z', source: 'ClinicianVerified', flag: 'High', range: '4-5.6' },
    { code: 'Sodium', value: 140, unit: 'mmol/L', takenAt: '2026-08-14T05:00:00Z', source: 'ClinicianVerified', flag: null, range: '135-145' },
  ],
  bmi: null, history: [], bloodGroup: null, careTeam: [],
};
const checklist: ChecklistItem[] = [{ id: 'history', state: 'done' }, { id: 'diagnoses', state: 'todo' }];
const text = (el: Element) => (el.textContent ?? '').replace(/[⁦-⁩]/g, '');

function Side(props: Partial<ChartPanelProps> & { data?: Patient360['chart'] | null }) {
  const [panel, setPanel] = useState<StudioPanel>('chart');
  return <ChartPanel variant="side" panel={panel} onPanel={setPanel} chart={{ data: props.data === undefined ? chart : props.data, loading: false, failed: false }}
    timeZone="Asia/Kolkata" checklist={checklist} assist={props.assist} />;
}

describe('ChartPanel (MDV-P1-094)', () => {
  it('shows Chart, Past visits and the checklist count; Assist only when passed', () => {
    const { rerender } = render(<Side />);
    expect(screen.getAllByRole('tab').map(text)).toEqual(['Chart', 'Past visits', 'Checklist 1/2']);
    rerender(<Side assist={<p data-testid="assist" />} />);
    expect(screen.getAllByRole('tab').map(text)).toEqual(['Assist', 'Chart', 'Past visits', 'Checklist 1/2']);
  });

  it('shows the chart in one view: allergies in words, active medicines and problems, results flagged in words', () => {
    render(<Side />);
    const panel = screen.getByRole('complementary', { name: 'Consultation tools' });
    expect(within(panel).getByText('Allergy: Penicillin (Patient reported)')).toBeTruthy();
    expect(within(panel).getByText('Metformin')).toBeTruthy();
    expect(within(panel).queryByText('Amoxicillin')).toBeNull();
    expect(within(panel).getByText('Type 2 diabetes')).toBeTruthy();
    expect(within(panel).queryByText('Old sprain')).toBeNull();
    expect(within(panel).getByText('High, usual 4-5.6 %', { normalizer: s => s.replace(/[⁦-⁩]/g, '') })).toBeTruthy();
    expect(within(panel).getByText('In range')).toBeTruthy();
  });

  it('keeps not recorded, no known allergies and listed apart', () => {
    const { rerender } = render(<Side data={{ ...chart, allergyState: 'NotRecorded', allergies: [] }} />);
    expect(screen.getByText('Allergies not recorded')).toBeTruthy();
    rerender(<Side data={{ ...chart, allergyState: 'NoKnownAllergies', allergies: [] }} />);
    expect(screen.getByText('No known allergies')).toBeTruthy();
    expect(screen.queryByText('Allergies not recorded')).toBeNull();
    // Listed but every entry cleared as "not an allergy": unclear, so not recorded, never none.
    rerender(<Side data={{ ...chart, allergies: [{ ...chart.allergies[0]!, status: 'NotAnAllergy' }] }} />);
    expect(screen.getByText('Allergies not recorded')).toBeTruthy();
  });

  it('lists only ended visits under Past visits', () => {
    render(<Side />);
    fireEvent.mouseDown(screen.getByRole('tab', { name: 'Past visits' }));
    expect(within(screen.getByRole('list', { name: 'Past visits' })).getAllByRole('listitem')).toHaveLength(1);
  });

  it('says loading and failed', () => {
    const { rerender } = render(<ChartPanel variant="side" panel="chart" onPanel={() => {}} chart={{ data: null, loading: true, failed: false }} timeZone={undefined} checklist={[]} />);
    expect(screen.getByRole('status').textContent).toBe('Opening the chart…');
    rerender(<ChartPanel variant="side" panel="chart" onPanel={() => {}} chart={{ data: null, loading: false, failed: true }} timeZone={undefined} checklist={[]} />);
    expect(screen.getByRole('alert').textContent).toBe('The chart could not be opened.');
  });

  it('opens as a drawer from its button and returns focus to the button on close', async () => {
    function Drawer() {
      const [open, setOpen] = useState(false);
      return <>
        <ChartDrawerButton open={open} onOpen={() => setOpen(true)} />
        <ChartPanel variant="drawer" open={open} onOpenChange={setOpen} panel="chart" onPanel={() => {}} chart={{ data: chart, loading: false, failed: false }} timeZone={undefined} checklist={[]} />
      </>;
    }
    render(<Drawer />);
    const button = screen.getByRole('button', { name: 'Chart' });
    expect(button.getAttribute('aria-expanded')).toBe('false');
    button.focus();
    fireEvent.click(button);
    const dialog = screen.getByRole('dialog', { name: 'Consultation tools' });
    expect(within(dialog).getByText('Metformin')).toBeTruthy();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Close' }));
    expect(screen.queryByRole('dialog')).toBeNull();
    await waitFor(() => expect(document.activeElement).toBe(button)); // Radix moves focus on unmount, after a tick
  });
});
