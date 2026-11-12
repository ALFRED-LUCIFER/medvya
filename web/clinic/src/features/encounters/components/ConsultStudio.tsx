import { useEffect, type MouseEvent, type ReactNode } from 'react';
import { Circle, CircleCheck } from 'lucide-react';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, Tabs, TabsContent, TabsList, TabsTrigger } from '@medvya/ui';
import type { Patient360 } from '../../patient360/domain/chart';
import { checklistIds, studioSections, templateOwnSections, type StudioSection } from '../domain/checklist';
import { planHeading, type StudioPanel } from '../domain/encounter';
import { templateNameKey } from '../domain/note';
import NoteAssist, { VitalList } from './NoteAssist';
import type { NoteEditorProps } from './NoteEditor';
import { NoteSectionField, SaveState, SignedBanner, TemplatePicker } from './NoteParts';
import { SignChecklist, SignedLine, SignNote } from './NoteSign';
import RecordingConsentChoice from './RecordingConsentChoice';

type Props = NoteEditorProps & {
  ticks: Record<StudioSection, boolean> | null;
  panel: StudioPanel; onPanel: (panel: StudioPanel) => void;
  chart: { data: Patient360['chart'] | null; loading: boolean; failed: boolean };
  /** The plan's actions (MDV-P2-043): medicines, the prescription, tests, advice, follow-up and referral, composed in by the route. */
  plan?: ReactNode;
  /** Vitals, weight and allergies recorded by the doctor (MDV audit P0-2), composed in by the route. */
  entry?: ReactNode;
  /** Finish visit, kept beside Sign in the one bar at the foot of the note (MDV audit P1-04); absent once the consultation has ended. */
  finish?: ReactNode;
};

const heading = (id: StudioSection) => `studio-${id}`;

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
  useEffect(() => { if (!p.loading && !showAssist && panel === 'assist') onPanel('chart'); }, [p.loading, showAssist, panel, onPanel]);
  // Arriving from "Plan change" (MDV-P2-037): focus the Plan once the note is there.
  const ready = !p.loading && !!p.template && !!p.ticks;
  useEffect(() => { if (ready && window.location.hash === `#${planHeading}`) document.getElementById(planHeading)?.focus(); }, [ready]);
  // Finish visit stays reachable when the note cannot be shown: the doctor can always end the consultation.
  if (p.loading) return <><p role="status">{t('noteEditor.loading')}</p>{p.finish}</>;
  if (p.loadFailed || !p.template || !p.templateKey || !p.ticks) return <><p role="alert" className="font-bold text-danger-700">{t(p.forbidden ? 'noteEditor.forbidden' : 'noteEditor.loadFailed')}</p>{p.finish}</>;
  const { template, ticks } = p;
  const section = (s: (typeof template.sections)[number]) => <NoteSectionField key={s.id} section={s} register={p.register} readOnly={p.readOnly} answers={p.answers} onAnswers={p.onAnswers} />;
  const field = (id: string) => template.sections.filter(s => s.id === id).map(section);
  const title = (id: StudioSection) => id === 'template' ? t(templateNameKey(template.key)) : t(`studio.section.${id}` as MessageKey);
  const done = p.checklist.filter(c => c.state !== 'todo').length;
  // Signed (MDV audit UX-11): only what was signed. Vitals is the draft's prefill (what was inserted is in Examination), and no section is "To do".
  const shown: readonly StudioSection[] = p.readOnly ? studioSections.filter(s => s !== 'vitals') : studioSections;
  const numbered = (id: StudioSection) => t('studio.numbered', { n: shown.indexOf(id) + 1, title: title(id) });
  return (
    <section aria-labelledby="note-title" className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 id="note-title" className="text-lg font-extrabold">{t('noteEditor.title')}</h2>
        {!p.readOnly && <SaveState phase={p.phase} reloading={p.reloading} onRetry={p.onRetry} onReapply={p.onReapply} />}
      </div>
      <div className="flex flex-col gap-2">
        <RecordingConsentChoice value={p.consent} readOnly={p.readOnly} onChange={p.onConsent} />
        {!p.readOnly && p.consent && <p className="text-sm font-bold">{t(`studio.consent.${p.consent}`)}</p>}
        {!p.readOnly && <p className="text-sm text-muted-foreground">{t('noteEditor.manualBanner')}</p>}
      </div>
      <SectionBar sections={shown} ticks={p.readOnly ? null : ticks} title={title} />
      <div className="grid items-start gap-4 lg:grid-cols-[minmax(0,1fr)_20rem]">
        <div className="flex min-w-0 flex-col gap-4">
          {!p.readOnly && <Numbered id="vitals" title={numbered}>
            {p.assist && p.assist.vitals.length > 0
              ? <div><Button variant="outline" className="min-h-11" onClick={p.assist.onInsertVitals}>{t('noteAssist.vitals.insert')}</Button></div>
              : <p className="text-sm text-muted-foreground">{t('noteAssist.vitals.empty')}</p>}
            {p.entry}
          </Numbered>}
          <Numbered id="history" title={numbered}>{field('chiefComplaint')}{field('subjective')}{field('reviewOfSystems')}</Numbered>
          <Numbered id="template" title={numbered}>
            <TemplatePicker templateKey={p.templateKey} choices={p.choices} readOnly={p.readOnly} pendingSwitch={p.pendingSwitch} suggested={p.suggested}
              onRequestSwitch={p.onRequestSwitch} onCancelSwitch={p.onCancelSwitch} onConfirmSwitch={p.onConfirmSwitch} />
            {templateOwnSections(template).length === 0
              ? <p className="text-sm text-muted-foreground">{t('studio.template.none')}</p>
              : templateOwnSections(template).map(section)}
          </Numbered>
          <Numbered id="exam" title={numbered}>{field('examination')}{field('objective')}</Numbered>
          <Numbered id="assessment" title={numbered}>{field('assessment')}</Numbered>
          <Numbered id="plan" title={numbered}>{field('plan')}{p.plan}</Numbered>
          {/* One bar that stays at the foot of the screen while the note is long: Sign is the primary, Finish visit is beside it. */}
          <Numbered id="sign" title={numbered} bar>
            <SignedBanner signed={p.readOnly} versionNo={p.signature?.versionNo ?? 1} />
            {p.signature && <SignedLine signature={p.signature} timeZone={p.timeZone} />}
            {!p.readOnly && !p.canSign && <p className="text-sm text-muted-foreground">{t('noteSign.onlyDoctor')}</p>}
            <div className="flex flex-wrap items-start gap-x-3 gap-y-2">
              {!p.readOnly && <SignNote canSign={p.canSign} remaining={p.remaining} patient={p.patient} doctorName={p.signAsName} signing={p.signing} confirming={p.confirming}
                problem={p.signProblem} onRequest={p.onRequestSign} onCancel={p.onCancelSign} onConfirm={p.onConfirmSign} />}
              {p.finish}
            </div>
          </Numbered>
        </div>
        <aside aria-label={t('studio.panel.label')} className="flex flex-col gap-3 rounded-xl border bg-card p-3 lg:sticky lg:top-16">
          {/* The nurse's readings stay beside the note, each with the nurse's flag in an icon and words (MDV-1090). */}
          {!p.readOnly && p.assist && p.assist.vitals.length > 0 && (
            <section aria-labelledby="studio-vitals" className="flex flex-col gap-1 text-sm">
              <h4 id="studio-vitals" className="font-bold">{t('studio.chart.vitals')}</h4>
              <VitalList vitals={p.assist.vitals} />
            </section>
          )}
          <Tabs value={panel} onValueChange={v => p.onPanel(v as StudioPanel)}>
            <TabsList className="h-auto w-full gap-1 rounded-xl border bg-card p-1">
              {showAssist && <TabsTrigger value="assist" className="min-h-11 flex-1 font-bold text-foreground underline-offset-8 data-[state=active]:underline">{t('studio.panel.assist')}</TabsTrigger>}
              <TabsTrigger value="chart" className="min-h-11 flex-1 font-bold text-foreground underline-offset-8 data-[state=active]:underline">{t('studio.panel.chart')}</TabsTrigger>
              {p.checklist.length > 0 && <TabsTrigger value="check" className="min-h-11 flex-1 font-bold text-foreground underline-offset-8 data-[state=active]:underline">{t('studio.panel.check', { done, total: checklistIds.length })}</TabsTrigger>}
            </TabsList>
            {showAssist && <TabsContent value="assist"><NoteAssist assist={p.assist!} template={template} timeZone={p.timeZone} showVitals={false} /></TabsContent>}
            <TabsContent value="chart"><ChartSummary {...p.chart} timeZone={p.timeZone} /></TabsContent>
            {p.checklist.length > 0 && <TabsContent value="check"><SignChecklist items={p.checklist} /></TabsContent>}
          </Tabs>
        </aside>
      </div>
    </section>
  );
}

/** A nav landmark that stays at the top; each link moves focus to its section's heading. Finished is an icon and words, never colour alone. */
function SectionBar({ sections, ticks, title }: { sections: readonly StudioSection[]; ticks: Record<StudioSection, boolean> | null; title: (id: StudioSection) => string }) {
  const { t } = useI18n();
  const go = (e: MouseEvent<HTMLAnchorElement>, id: StudioSection) => {
    e.preventDefault();
    document.getElementById(heading(id))?.focus();
  };
  return (
    <nav aria-label={t('studio.nav')} className="sticky top-0 z-10 -mx-1 bg-background px-1 py-1">
      <ol className="flex gap-1.5 overflow-x-auto pb-1 lg:flex-wrap lg:overflow-visible">
        {sections.map((id, i) => (
          <li key={id} className="shrink-0">
            <a href={`#${heading(id)}`} onClick={e => go(e, id)} className="relative inline-flex min-h-11 items-center gap-1.5 rounded-full border bg-card px-3 text-sm font-bold focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring">
              {ticks && (ticks[id] ? <CircleCheck aria-hidden className="size-4 text-success-700" /> : <Circle aria-hidden className="size-4 text-muted-foreground" />)}
              {t('studio.numbered', { n: i + 1, title: title(id) })}
              {ticks && <span className="sr-only">{t(ticks[id] ? 'signChecklist.state.done' : 'signChecklist.state.todo')}</span>}
            </a>
          </li>
        ))}
      </ol>
    </nav>
  );
}

function Numbered({ id, title, bar, children }: { id: StudioSection; title: (id: StudioSection) => string; bar?: boolean; children: ReactNode }) {
  return (
    <div className={bar ? 'sticky bottom-0 z-10 flex flex-col gap-2 rounded-xl border bg-card p-3 shadow-lg' : 'flex flex-col gap-3 rounded-xl border bg-card p-4'}>
      <h3 id={heading(id)} tabIndex={-1} className="scroll-mt-20 font-extrabold focus:outline-none focus-visible:outline-2 focus-visible:outline-ring">
        {title(id)}
      </h3>
      {children}
    </div>
  );
}

/** The chart at a glance while writing: active problems and medicines, as Patient 360 words them. */
function ChartSummary({ data, loading, failed, timeZone }: Props['chart'] & { timeZone: string | undefined }) {
  const { t, formatDate } = useI18n();
  if (failed) return <p role="alert" className="text-sm font-bold text-danger-700">{t('patient360.loadFailed')}</p>;
  if (loading || !data) return <p role="status" className="text-sm">{t('patient360.loading')}</p>;
  const problems = data.problems.filter(x => x.status === 'Active');
  const medicines = data.medicines.filter(x => x.state === 'Active');
  const past = data.recentEncounters.filter(x => x.status === 'Ended'); // the open one is this visit
  return (
    <div className="flex flex-col gap-3 text-sm">
      <div>
        <h4 className="font-bold">{t('patient360.problems')}</h4>
        {problems.length === 0 ? <p className="text-muted-foreground">{t('patient360.problems.empty')}</p>
          : <ul className="list-disc ps-5">{problems.map((x, i) => <li key={i}>{x.description}{x.icd10Code && <span className="ms-1 text-muted-foreground" dir="ltr">{x.icd10Code}</span>}</li>)}</ul>}
      </div>
      <div>
        <h4 className="font-bold">{t('patient360.medicines')}</h4>
        {medicines.length === 0 ? <p className="text-muted-foreground">{t('patient360.medicines.empty')}</p>
          : <ul className="list-disc ps-5">{medicines.map((x, i) => <li key={i}>{x.dose ? `${x.name} ${x.dose}` : x.name}</li>)}</ul>}
      </div>
      <div>
        <h4 className="font-bold">{t('studio.chart.recent')}</h4>
        {past.length === 0 ? <p className="text-muted-foreground">{t('studio.chart.recent.none')}</p>
          : <ul className="list-disc ps-5">{past.map(x => <li key={x.id}>{formatDate(new Date(x.startedAt), timeZone)}</li>)}</ul>}
      </div>
    </div>
  );
}

