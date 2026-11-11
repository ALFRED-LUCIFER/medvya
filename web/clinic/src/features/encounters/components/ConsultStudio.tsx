import { useEffect, type MouseEvent, type ReactNode } from 'react';
import { Circle, CircleCheck } from 'lucide-react';
import { useI18n, type MessageKey } from '@medvya/i18n';
import { Button, Tabs, TabsContent, TabsList, TabsTrigger } from '@medvya/ui';
import type { Patient360 } from '../../patient360/domain/chart';
import { checklistIds, studioSections, templateOwnSections, type StudioSection } from '../domain/checklist';
import type { StudioPanel } from '../domain/encounter';
import { templateNameKey } from '../domain/note';
import NoteAssist from './NoteAssist';
import type { NoteEditorProps } from './NoteEditor';
import { SaveState, SectionField, SignedBanner, TemplatePicker } from './NoteParts';
import { SignChecklist, SignedLine, SignNote } from './NoteSign';
import RecordingConsentChoice from './RecordingConsentChoice';

type Props = NoteEditorProps & {
  ticks: Record<StudioSection, boolean> | null;
  panel: StudioPanel; onPanel: (panel: StudioPanel) => void;
  chart: { data: Patient360['chart'] | null; loading: boolean; failed: boolean };
};

const heading = (id: StudioSection) => `studio-${id}`;

/**
 * E-04 one page (MDV-P2-041, CONSULTATION STUDIO): the recording consent first, a section bar that stays at the top, numbered sections over
 * today's template and a side panel (Assist, chart summary, checklist). The same form, autosave and sign as the guided steps.
 * shortcut: History, Examination and Plan hold today's text sections; the structured review of systems and examination (MDV-P2-042) and the
 * plan actions with Add medicine (MDV-P2-043, MDV-P2-039) go inside those sections when they land.
 */
export default function ConsultStudio(p: Props) {
  const { t } = useI18n();
  // Assist is off when the patient declined recording; the typed note works as today, and the panel moves to the chart (read when shown).
  const showAssist = !!p.assist && p.consent !== 'Declined';
  const { panel, onPanel } = p;
  useEffect(() => { if (!p.loading && !showAssist && panel === 'assist') onPanel('chart'); }, [p.loading, showAssist, panel, onPanel]);
  if (p.loading) return <p role="status">{t('noteEditor.loading')}</p>;
  if (p.loadFailed || !p.template || !p.templateKey || !p.ticks) return <p role="alert" className="font-bold text-danger-700">{t(p.forbidden ? 'noteEditor.forbidden' : 'noteEditor.loadFailed')}</p>;
  const { template, ticks } = p;
  const field = (id: string) => template.sections.filter(s => s.id === id).map(s => <SectionField key={s.id} section={s} register={p.register} readOnly={p.readOnly} />);
  const title = (id: StudioSection) => id === 'template' ? t(templateNameKey(template.key)) : t(`studio.section.${id}` as MessageKey);
  const done = p.checklist.filter(c => c.state !== 'todo').length;
  return (
    <section aria-labelledby="note-title" className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 id="note-title" className="text-lg font-extrabold">{t('noteEditor.title')}</h2>
        {!p.readOnly && <SaveState phase={p.phase} reloading={p.reloading} onRetry={p.onRetry} onReapply={p.onReapply} />}
      </div>
      <div className="flex flex-col gap-2">
        <RecordingConsentChoice value={p.consent} readOnly={p.readOnly} onChange={p.onConsent} />
        {p.consent && <p className="text-sm font-bold">{t(`studio.consent.${p.consent}`)}</p>}
        <p className="text-sm text-muted-foreground">{t('noteEditor.manualBanner')}</p>
      </div>
      <SectionBar ticks={ticks} title={title} />
      <div className="grid items-start gap-4 lg:grid-cols-[minmax(0,1fr)_20rem]">
        <div className="flex min-w-0 flex-col gap-4">
          <Numbered id="vitals" title={title}>
            {p.assist && p.assist.vitals.length > 0
              ? <>
                <ul className="list-disc ps-5 text-sm">{p.assist.vitals.map(line => <li key={line}>{line}</li>)}</ul>
                <div><Button variant="outline" className="min-h-11" onClick={p.assist.onInsertVitals}>{t('noteAssist.vitals.insert')}</Button></div>
              </>
              : <p className="text-sm text-muted-foreground">{t('noteAssist.vitals.empty')}</p>}
          </Numbered>
          <Numbered id="history" title={title}>{field('chiefComplaint')}{field('subjective')}</Numbered>
          <Numbered id="template" title={title}>
            <TemplatePicker templateKey={p.templateKey} choices={p.choices} readOnly={p.readOnly} pendingSwitch={p.pendingSwitch}
              onRequestSwitch={p.onRequestSwitch} onCancelSwitch={p.onCancelSwitch} onConfirmSwitch={p.onConfirmSwitch} />
            {templateOwnSections(template).length === 0
              ? <p className="text-sm text-muted-foreground">{t('studio.template.none')}</p>
              : templateOwnSections(template).map(s => <SectionField key={s.id} section={s} register={p.register} readOnly={p.readOnly} />)}
          </Numbered>
          <Numbered id="exam" title={title}>{field('objective')}</Numbered>
          <Numbered id="assessment" title={title}>{field('assessment')}</Numbered>
          <Numbered id="plan" title={title}>{field('plan')}</Numbered>
          <Numbered id="sign" title={title}>
            <SignedBanner signed={p.readOnly} versionNo={p.signature?.versionNo ?? 1} />
            {p.signature && <SignedLine signature={p.signature} timeZone={p.timeZone} />}
            {!p.readOnly && !p.canSign && <p className="text-sm text-muted-foreground">{t('noteSign.onlyDoctor')}</p>}
            {!p.readOnly && <SignNote canSign={p.canSign} remaining={p.remaining} patient={p.patient} doctorName={p.signAsName} signing={p.signing} confirming={p.confirming}
              problem={p.signProblem} onRequest={p.onRequestSign} onCancel={p.onCancelSign} onConfirm={p.onConfirmSign} />}
          </Numbered>
        </div>
        <aside aria-label={t('studio.panel.label')} className="flex flex-col gap-3 rounded-xl border bg-card p-3 lg:sticky lg:top-16">
          <Tabs value={panel} onValueChange={v => p.onPanel(v as StudioPanel)}>
            <TabsList className="h-auto w-full gap-1 rounded-xl border bg-card p-1">
              {showAssist && <TabsTrigger value="assist" className="min-h-11 flex-1 font-bold text-foreground underline-offset-8 data-[state=active]:underline">{t('studio.panel.assist')}</TabsTrigger>}
              <TabsTrigger value="chart" className="min-h-11 flex-1 font-bold text-foreground underline-offset-8 data-[state=active]:underline">{t('studio.panel.chart')}</TabsTrigger>
              {p.checklist.length > 0 && <TabsTrigger value="check" className="min-h-11 flex-1 font-bold text-foreground underline-offset-8 data-[state=active]:underline">{t('studio.panel.check', { done, total: checklistIds.length })}</TabsTrigger>}
            </TabsList>
            {showAssist && <TabsContent value="assist"><NoteAssist assist={p.assist!} template={template} timeZone={p.timeZone} showVitals={false} /></TabsContent>}
            <TabsContent value="chart"><ChartSummary {...p.chart} /></TabsContent>
            {p.checklist.length > 0 && <TabsContent value="check"><SignChecklist items={p.checklist} /></TabsContent>}
          </Tabs>
        </aside>
      </div>
    </section>
  );
}

/** A nav landmark that stays at the top; each link moves focus to its section's heading. Finished is an icon and words, never colour alone. */
function SectionBar({ ticks, title }: { ticks: Record<StudioSection, boolean>; title: (id: StudioSection) => string }) {
  const { t } = useI18n();
  const go = (e: MouseEvent<HTMLAnchorElement>, id: StudioSection) => {
    e.preventDefault();
    document.getElementById(heading(id))?.focus();
  };
  return (
    <nav aria-label={t('studio.nav')} className="sticky top-0 z-10 -mx-1 bg-background px-1 py-1">
      <ol className="flex gap-1.5 overflow-x-auto pb-1 lg:flex-wrap lg:overflow-visible">
        {studioSections.map((id, i) => (
          <li key={id} className="shrink-0">
            <a href={`#${heading(id)}`} onClick={e => go(e, id)} className="relative inline-flex min-h-11 items-center gap-1.5 rounded-full border bg-card px-3 text-sm font-bold focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring">
              {ticks[id] ? <CircleCheck aria-hidden className="size-4 text-success-700" /> : <Circle aria-hidden className="size-4 text-muted-foreground" />}
              {t('studio.numbered', { n: i + 1, title: title(id) })}
              <span className="sr-only">{t(ticks[id] ? 'signChecklist.state.done' : 'signChecklist.state.todo')}</span>
            </a>
          </li>
        ))}
      </ol>
    </nav>
  );
}

function Numbered({ id, title, children }: { id: StudioSection; title: (id: StudioSection) => string; children: ReactNode }) {
  const { t } = useI18n();
  return (
    <div className="flex flex-col gap-3 rounded-xl border bg-card p-4">
      <h3 id={heading(id)} tabIndex={-1} className="scroll-mt-20 font-extrabold focus:outline-none focus-visible:outline-2 focus-visible:outline-ring">
        {t('studio.numbered', { n: studioSections.indexOf(id) + 1, title: title(id) })}
      </h3>
      {children}
    </div>
  );
}

/** The chart at a glance while writing: active problems and medicines, as Patient 360 words them. */
function ChartSummary({ data, loading, failed }: Props['chart']) {
  const { t } = useI18n();
  if (failed) return <p role="alert" className="text-sm font-bold text-danger-700">{t('patient360.loadFailed')}</p>;
  if (loading || !data) return <p role="status" className="text-sm">{t('patient360.loading')}</p>;
  const problems = data.problems.filter(x => x.status === 'Active');
  const medicines = data.medicines.filter(x => x.state === 'Active');
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
    </div>
  );
}

