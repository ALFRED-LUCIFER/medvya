import type { ReactNode } from 'react';
import { CircleStop, PenLine, Sparkles } from 'lucide-react';
import {
  alertLooks, Button, ClinicalAlert, IdentityBanner, Input, Label, provenanceLooks, ProvenanceTag,
  RecordingPill, recordingLooks, StatusChip, statusLooks, SystemState, systemStates,
} from '@medvya/ui';

const keys = <T extends object>(o: T) => Object.keys(o) as (keyof T)[];

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-lg font-bold">{title}</h2>
      <div className="flex flex-wrap items-center gap-3">{children}</div>
    </section>
  );
}

function Field({ id, label, defaultValue, error, disabled }: { id: string; label: string; defaultValue: string; error?: string; disabled?: boolean }) {
  return (
    <div className="flex w-56 flex-col gap-1.5">
      <Label htmlFor={id}>{label}</Label>
      <Input id={id} defaultValue={defaultValue} disabled={disabled} aria-invalid={!!error} aria-describedby={error ? `${id}-err` : undefined} className="min-h-11" />
      {error && <p id={`${id}-err`} className="text-xs font-semibold text-danger-700">{error}</p>}
    </div>
  );
}

// Every primitive in every state (MDV-P0-020). Synthetic data only.
// Dev-only page: its sample text is exempt from react/jsx-no-literals (.oxlintrc.json).
export default function UiGallery({ switcher }: { switcher: ReactNode }) {
  return (
    <main className="mx-auto flex max-w-[1280px] flex-col gap-8 p-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <h1 className="text-2xl font-bold">Medvya UI primitives</h1>{switcher}
      </div>
      <Section title="Patient identity banner">
        <IdentityBanner patient={{ name: 'Meera Iyer', age: 54, sex: 'F', mrn: 'DQ-10-4482', dob: '12 Mar 1972',
          allergies: [{ name: 'Penicillin', status: 'verified' }], consent: 'given', encounter: 'Encounter 04 Oct 2026 · OPD Room 3', synthetic: true }} />
        <IdentityBanner patient={{ name: 'Deepak Nair', age: 47, sex: 'M', mrn: 'DQ-11-1093', dob: '14 Aug 1979', allergies: [], synthetic: true }} />
        <IdentityBanner patient={{ name: 'Fatima Al Mansoori', age: 38, sex: 'F', mrn: 'DQ-AE-0458', dob: '03 Feb 1988', allergies: null, consent: 'notAsked', synthetic: true }} />
      </Section>
      <Section title="Buttons">
        <Button><PenLine aria-hidden />Sign note</Button>
        <Button loading>Signing…</Button>
        <Button disabled>Sign note (2 unresolved)</Button>
        <Button variant="outline">Save draft</Button>
        <Button variant="ai"><Sparkles aria-hidden />Generate draft</Button>
        <Button variant="destructive"><CircleStop aria-hidden />Stop recording</Button>
        <Button variant="navy">Request access</Button>
      </Section>
      <Section title="Closed-loop status">{keys(statusLooks).map(s => <StatusChip key={s} state={s} />)}</Section>
      <Section title="Provenance">{keys(provenanceLooks).map(s => <ProvenanceTag key={s} source={s} />)}</Section>
      <Section title="Recording and consent">{keys(recordingLooks).map(s => <RecordingPill key={s} state={s} elapsedSeconds={252} />)}</Section>
      <Section title="Alerts">
        {keys(alertLooks).map(s => <div key={s} className="min-w-80"><ClinicalAlert severity={s}>Synthetic example text</ClinicalAlert></div>)}
      </Section>
      <Section title="System states (F-05)">
        {keys(systemStates).map(s => <SystemState key={s} state={s} facility="Iyer Family Clinic · Adyar" onAction={() => {}} />)}
      </Section>
      <Section title="Text fields">
        <Field id="f-name" label="Full name" defaultValue="Deepak Nair" />
        <Field id="f-pin" label="PIN code" defaultValue="68203" error="PIN code must be 6 digits" />
        <Field id="f-locked" label="Locked" defaultValue="Locked" disabled />
      </Section>
    </main>
  );
}
