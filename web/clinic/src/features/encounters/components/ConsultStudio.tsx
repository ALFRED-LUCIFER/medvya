import { useEffect, useState, type MouseEvent, type ReactNode } from 'react';
import { Circle, CircleCheck } from 'lucide-react';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, StickyActionBar } from '@medvya/ui';
import type { Patient360 } from '../../patient360/domain/chart';
import ChartPanel, { ChartDrawerButton } from './ChartPanel';
import { studioSections, templateOwnSections, type StudioSection } from '../domain/checklist';
import { planHeading, type StudioPanel } from '../domain/encounter';
import { recordingAvailable, templateNameKey } from '../domain/note';
import NoteAssist, { VitalList } from './NoteAssist';
import type { NoteEditorProps } from './NoteEditor';
import { NoteSectionField, SaveState, SignedBanner, TemplatePicker } from './NoteParts';
import { SignedLine, SignNote } from './NoteSign';
import RecordingConsentChoice from './RecordingConsentChoice';

type Props = NoteEditorProps & {
  ticks: Record<StudioSection, boolean> | null;
  panel: StudioPanel; onPanel: (panel: StudioPanel) => void;
  chart: { data: Patient360['chart'] | null; loading: boolean; failed: boolean };
  /** The plan's actions (MDV-P2-043): medicines, the prescription, tests, advice, follow-up and referral, composed in by the route. */
  plan?: ReactNode;
  /** Vitals, weight and allergies recorded by the doctor (MDV audit P0-2), composed in by the route. */
  entry?: ReactNode;
  /** This consultation's coded diagnoses, added inline in Assessment with no route change (MDV-P1-094), composed in by the route. */
  diagnoses?: ReactNode;
  /** The draft prescription (MDV-P1-094: directly after Assessment), composed in by the route; its section shows only when given. */
  prescription?: ReactNode;
};

/** The page's sections: the note's own, plus the prescription, which has no tick (only the note's sections are ticked). */
type Anchor = StudioSection | 'prescription';

const heading = (id: Anchor) => `studio-${id}`;

/**
 * E-04 one page (MDV-P2-041, CONSULTATION STUDIO): the recording consent first, a section bar that stays at the top, numbered sections over
 * today's template and a side panel (Assist, chart summary, checklist). The same form, autosave and sign as the guided steps.
 * History holds the review of systems and Examination the examination by system when the template has them (MDV-P2-042).
 * Plan holds its text and the plan's actions (MDV-P2-043), composed in by the route.
 */
export default function ConsultStudio(p: Props) {
  const { t } = useI18n();
  // Assist is off when the patient declined recording; the typed note works as today, and the panel moves to the chart (read when shown).
  const showAssist = !!p.assist && p.consent !== 'Declined';
  const { panel, onPanel } = p;
  const [drawer, setDrawer] = useState(false);
  const assist = showAssist && !p.loading && p.template ? <NoteAssist assist={p.assist!} template={p.template} timeZone={p.timeZone} showVitals={false} /> : undefined;
  useEffect(() => { if (!p.loading && !showAssist && panel === 'assist') onPanel('chart'); }, [p.loading, showAssist, panel, onPanel]);
  // Arriving from "Plan change" (MDV-P2-037): focus the Plan once the note is there.
  const ready = !p.loading && !!p.template && !!p.ticks;
  useEffect(() => { if (ready && window.location.hash === `#${planHeading}`) document.getElementById(planHeading)?.focus(); }, [ready]);
  // Finish visit stays reachable when the note cannot be shown: it is in the footer (ConsultFooter), not here.
  // The prescription is its own record: it stays usable while the note loads or when the note cannot be shown.
  if (p.loading) return <><p role="status">{t('noteEditor.loading')}</p>{p.prescription}</>;
  if (p.loadFailed || !p.template || !p.templateKey || !p.ticks) return <><p role="alert" className="font-bold text-danger-700">{t(p.forbidden ? 'noteEditor.forbidden' : 'noteEditor.loadFailed')}</p>{p.prescription}</>;
  const { template, ticks } = p;
  const section = (s: (typeof template.sections)[number]) => <NoteSectionField key={s.id} section={s} register={p.register} readOnly={p.readOnly} answers={p.answers} onAnswers={p.onAnswers} source={s.id === 'chiefComplaint' ? p.complaintSource : null} />;
  const field = (id: string) => template.sections.filter(s => s.id === id).map(section);
  const title = (id: Anchor) => id === 'template' ? t(templateNameKey(template.key)) : t(`studio.section.${id}` as MessageKey);
  // Signed (MDV audit UX-11): only what was signed. Vitals is the draft's prefill (what was inserted is in Examination), and no section is "To do".
  const noteSections = p.readOnly ? studioSections.filter(s => s !== 'vitals') : studioSections;
  const shown: readonly Anchor[] = p.prescription ? noteSections.flatMap(s => s === 'assessment' ? [s, 'prescription' as const] : [s]) : noteSections;
  const numbered = (id: Anchor) => t('studio.numbered', { n: shown.indexOf(id) + 1, title: title(id) });
  return (
    <section aria-labelledby="note-title" className="flex flex-col gap-4">
      {/* MDV-P1-094: the strip and the section bar already say where the doctor is; the title stays for screen readers. */}
      <h2 id="note-title" className="sr-only">{t('noteEditor.title')}</h2>
      <div className="flex flex-col gap-2">
        <RecordingConsentChoice value={p.consent} readOnly={p.readOnly} onChange={p.onConsent} />
        {!p.readOnly && recordingAvailable && p.consent && <p className="text-sm font-bold">{t(`studio.consent.${p.consent}`)}</p>}
        {!p.readOnly && <p className="text-sm text-muted-foreground">{t('noteEditor.manualBanner')}</p>}
      </div>
      <SectionBar sections={shown} ticks={p.readOnly ? null : ticks} title={title} chart={<ChartDrawerButton open={drawer} onOpen={() => setDrawer(true)} />} />
      {/* MDV-P1-094: the note column at most 860 px, the panel 360 px beside it from 1024 px. */}
      <div className="grid items-start gap-6 wide:grid-cols-[minmax(0,53.75rem)_22.5rem]">
        <div className="flex min-w-0 flex-col gap-6">
          {!p.readOnly && <Numbered id="vitals" title={numbered}>
            {/* The nurse's readings in one row, each with the nurse's flag in an icon and words (MDV-1090). */}
            {p.assist && p.assist.vitals.length > 0
              ? <div className="flex flex-wrap items-center gap-2"><VitalList vitals={p.assist.vitals} row />
                <Button variant="outline" size="sm" onClick={p.assist.onInsertVitals}>{t('noteAssist.vitals.insert')}</Button></div>
              : <p className="text-sm text-muted-foreground">{t('noteAssist.vitals.empty')}</p>}
            {p.entry}
          </Numbered>}
          <Numbered id="history" title={numbered}>
            {/* Side by side from 1024 px, as the blueprint's workspace; each keeps its own label and full width on a tablet. */}
            <div className="grid gap-3 wide:grid-cols-2">{field('chiefComplaint')}{field('subjective')}</div>
            {field('reviewOfSystems')}
          </Numbered>
          <Numbered id="template" title={numbered}>
            <TemplatePicker templateKey={p.templateKey} choices={p.choices} readOnly={p.readOnly} pendingSwitch={p.pendingSwitch} suggested={p.suggested}
              onRequestSwitch={p.onRequestSwitch} onCancelSwitch={p.onCancelSwitch} onConfirmSwitch={p.onConfirmSwitch} />
            {templateOwnSections(template).length > 0 ? templateOwnSections(template).map(section)
              : <p className="text-sm text-muted-foreground">{t('studio.template.none')}</p>}
          </Numbered>
          <Numbered id="exam" title={numbered}>{field('examination')}{field('objective')}</Numbered>
          <Numbered id="assessment" title={numbered}>{field('assessment')}{p.diagnoses}</Numbered>
          {p.prescription && <Numbered id="prescription" title={numbered}>{p.prescription}</Numbered>}
          <Numbered id="plan" title={numbered}>{field('plan')}{p.plan}</Numbered>
          {/* Review and sign: what was signed, or who may sign. Sign itself is in the layout footer (ConsultFooter). */}
          <Numbered id="sign" title={numbered}>
            <SignedBanner signed={p.readOnly} versionNo={p.signature?.versionNo ?? 1} />
            {p.signature && <SignedLine signature={p.signature} timeZone={p.timeZone} />}
            {!p.readOnly && <p className="text-sm text-muted-foreground">{t(p.canSign ? 'studio.sign.inBar' : 'noteSign.onlyDoctor')}</p>}
          </Numbered>
        </div>
        {/* At 1024 px and wider the chart panel is beside the note; narrower it is a drawer opened from the section bar (MDV-P1-094). */}
        <div className="hidden wide:block wide:self-stretch"><ChartPanel variant="side" panel={panel} onPanel={p.onPanel} chart={p.chart} timeZone={p.timeZone} checklist={p.checklist} assist={assist} /></div>
        <ChartPanel variant="drawer" open={drawer} onOpenChange={setDrawer} panel={panel} onPanel={p.onPanel} chart={p.chart} timeZone={p.timeZone} checklist={p.checklist} assist={assist} />
      </div>
    </section>
  );
}

/**
 * The layout footer of the one page (MDV-P1-092): the save state, Sign (the primary) and Finish visit, pinned at the foot of the screen for
 * the whole consultation and never over a field (StickyActionBar reserves its space). Finish visit stays when the note cannot be shown.
 */
export function ConsultFooter(p: NoteEditorProps & { finish?: ReactNode }) {
  const note = !p.loading && !p.loadFailed && !p.readOnly;
  if (!note && !p.finish) return null;
  return (
    <StickyActionBar status={note && <SaveState phase={p.phase} savedAt={p.savedAt} reloading={p.reloading} onRetry={p.onRetry} onReapply={p.onReapply} />}>
      {note && <SignNote canSign={p.canSign} remaining={p.remaining} patient={p.patient} doctorName={p.signAsName} signing={p.signing} confirming={p.confirming}
        problem={p.signProblem} onRequest={p.onRequestSign} onCancel={p.onCancelSign} onConfirm={p.onConfirmSign} resume={p.signResume} />}
      {p.finish}
    </StickyActionBar>
  );
}

/** A nav landmark that stays at the top; each link moves focus to its section's heading. Finished is an icon and words, never colour alone. */
function SectionBar({ sections, ticks, title, chart }: { sections: readonly Anchor[]; ticks: Record<StudioSection, boolean> | null; title: (id: Anchor) => string; chart: ReactNode }) {
  const { t } = useI18n();
  const go = (e: MouseEvent<HTMLAnchorElement>, id: Anchor) => {
    e.preventDefault();
    document.getElementById(heading(id))?.focus();
  };
  return (
    <nav aria-label={t('studio.nav')} data-pinned="top" className="flex items-center gap-2 sticky top-[var(--patient-strip-bottom,0px)] z-10 -mx-1 bg-background px-1 py-1 [@media(max-height:480px)]:static">
      {/* One row that scrolls sideways at every width (MDV-P1-094): a wrapped second row would cover the panel's top and add pinned height. */}
      <ol className="flex min-w-0 flex-1 gap-1.5 overflow-x-auto pb-1">
        {sections.map((id, i) => {
          const tick = id === 'prescription' || !ticks ? null : ticks[id];
          return (
          <li key={id} className="shrink-0">
            {/* Chromium leaves a half-hidden link half-hidden on focus; bring it fully into the scrolling row (WCAG 2.4.11). */}
            <a href={`#${heading(id)}`} onClick={e => go(e, id)} onFocus={e => e.currentTarget.scrollIntoView({ block: 'nearest', inline: 'nearest' })} className="relative inline-flex min-h-11 items-center gap-1.5 rounded-full border bg-card px-3 text-sm font-bold focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring">
              {tick !== null && (tick ? <CircleCheck aria-hidden className="size-4 text-success-700" /> : <Circle aria-hidden className="size-4 text-muted-foreground" />)}
              {t('studio.numbered', { n: i + 1, title: title(id) })}
              {tick !== null && <span className="sr-only">{t(tick ? 'signChecklist.state.done' : 'signChecklist.state.todo')}</span>}
            </a>
          </li>
          );
        })}
      </ol>
      <div className="shrink-0 wide:hidden">{chart}</div>
    </nav>
  );
}

/** One section of the page: a heading the section bar focuses, then its fields; no card around it (MDV-P1-094), so the note reads as one page. */
function Numbered({ id, title, children }: { id: Anchor; title: (id: Anchor) => string; children: ReactNode }) {
  return (
    <div className="flex min-w-0 flex-col gap-3">
      <h3 id={heading(id)} tabIndex={-1} className="scroll-mt-20 font-extrabold focus:outline-none focus-visible:outline-2 focus-visible:outline-ring">
        {title(id)}
      </h3>
      {children}
    </div>
  );
}
