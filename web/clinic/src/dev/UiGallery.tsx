import { type ReactNode, useState } from 'react';
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

// Class names are written out in full so Tailwind generates them (MDV-P1-079 tokens).
const typeTokens = ['text-display', 'text-title', 'text-heading', 'text-subheading', 'text-body', 'text-body-large', 'text-label', 'text-caption', 'text-mono font-mono'];
const spaceTokens = ['w-0.5', 'w-1', 'w-1.5', 'w-2', 'w-3', 'w-4', 'w-6', 'w-8', 'w-12', 'w-16'];
const sizeTokens = ['w-topbar', 'w-topbar-phone', 'w-rail', 'w-sidebar', 'w-header', 'w-gutter', 'w-gutter-md', 'w-gutter-xl', 'w-control', 'w-control-compact', 'w-row', 'w-row-compact'];
const widthTokens = ['max-w-page', 'max-w-reading', 'max-w-measure', 'max-w-form', 'max-w-panel', 'max-w-panel-chart'];

// Shows what the browser computed, so the specimen measures the token rather than restating it.
function Measured({ name, className = name, read, children }: { name: string; className?: string; read: (s: CSSStyleDeclaration) => string; children?: ReactNode }) {
  const [value, setValue] = useState('');
  return (
    <div className="flex flex-wrap items-baseline gap-3">
      <code className="w-56 shrink-0 text-xs">{name}</code>
      <span className="w-44 shrink-0 text-xs text-muted-foreground tabular-nums">{value}</span>
      <div ref={el => { if (el) setValue(read(getComputedStyle(el))); }} className={className}>{children}</div>
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
        <Button variant="default">Request access</Button>
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
      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-bold">Type tokens</h2>
        {typeTokens.map(c => <Measured key={c} name={c} read={s => `${s.fontSize} / ${s.lineHeight} · ${s.fontWeight}`}>Meera Iyer · DQ-10-4482 · ميرا</Measured>)}
      </section>
      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-bold">Spacing scale and layout sizes</h2>
        {[...spaceTokens, ...sizeTokens].map(c => <Measured key={c} name={c} className={`${c} h-4 shrink-0 bg-brand-700`} read={s => s.width} />)}
      </section>
      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-bold">Widths</h2>
        {widthTokens.map(c => <Measured key={c} name={c} read={s => s.maxWidth}>{c}</Measured>)}
        <Measured name="wide:block" className="hidden wide:block" read={s => s.display}>Shown from the wide breakpoint (1024 px)</Measured>
      </section>
      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-bold">Radius, elevation and motion</h2>
        {['rounded-sm', 'rounded-md', 'rounded-lg', 'rounded-full', 'shadow-popover', 'shadow-dialog'].map(c =>
          <Measured key={c} name={c} className={`${c} size-11 shrink-0 border bg-card`} read={s => (c.startsWith('shadow') ? 'elevation' : s.borderRadius)} />)}
        <Measured name="duration-fast ease-out" className="transition-colors duration-fast ease-out" read={s => `${s.transitionDuration} ${s.transitionTimingFunction}`}>Motion</Measured>
      </section>
    </main>
  );
}
