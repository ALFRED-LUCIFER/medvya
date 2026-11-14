import type { ReactNode } from 'react';
import { ArrowDown, ArrowUp, PanelRight } from 'lucide-react';
import { ltr, useI18n } from '@medvya/i18n';
import { AllergyBlock, Button, Sheet, SheetContent, SheetTitle, Tabs, TabsContent, TabsList, TabsTrigger } from '@medvya/ui';
import { bannerAllergyState, resultRows, type Patient360 } from '../../patient360/domain/chart';
import type { ChecklistItem } from '../domain/checklist';
import type { StudioPanel } from '../domain/encounter';
import { SignChecklist } from './NoteSign';

export type ChartPanelProps = {
  panel: StudioPanel; onPanel: (panel: StudioPanel) => void;
  chart: { data: Patient360['chart'] | null; loading: boolean; failed: boolean };
  timeZone: string | undefined;
  checklist: ChecklistItem[];
  /** NoteAssist, composed in by the page; the Assist tab shows only when it is passed (not when recording was declined). */
  assist?: ReactNode;
} & (
  | { variant: 'side' }
  /** 768 to 1023 px: a drawer opened by ChartDrawerButton; focus returns to the button and the page keeps its place on close. */
  | { variant: 'drawer'; open: boolean; onOpenChange: (open: boolean) => void }
);

const tab = 'min-h-11 flex-1 font-bold text-foreground underline-offset-8 data-[state=active]:underline';

/**
 * The consultation chart panel (MDV-P1-094, blueprint section 8): Assist, Chart, Past visits and Checklist beside the note, or in a drawer
 * on a tablet. The chart reads as Patient 360 and the patient strip word it: allergies in the strip's words and icons, results out of range in words.
 */
export default function ChartPanel(p: ChartPanelProps) {
  const { t } = useI18n();
  const done = p.checklist.filter(c => c.state !== 'todo').length;
  const tabs = (
    <Tabs value={p.panel} onValueChange={v => p.onPanel(v as StudioPanel)}>
      <TabsList className="h-auto w-full gap-1 rounded-xl border bg-card p-1">
        {p.assist && <TabsTrigger value="assist" className={tab}>{t('studio.panel.assist')}</TabsTrigger>}
        <TabsTrigger value="chart" className={tab}>{t('studio.panel.chart')}</TabsTrigger>
        <TabsTrigger value="past" className={tab}>{t('studio.panel.past')}</TabsTrigger>
        {p.checklist.length > 0 && <TabsTrigger value="check" className={tab}>{t('studio.panel.check', { done, total: p.checklist.length })}</TabsTrigger>}
      </TabsList>
      {p.assist && <TabsContent value="assist">{p.assist}</TabsContent>}
      <TabsContent value="chart"><ChartView {...p.chart} timeZone={p.timeZone} /></TabsContent>
      <TabsContent value="past"><PastVisits {...p.chart} timeZone={p.timeZone} /></TabsContent>
      {p.checklist.length > 0 && <TabsContent value="check"><SignChecklist items={p.checklist} /></TabsContent>}
    </Tabs>
  );
  if (p.variant === 'drawer') return (
    <Sheet open={p.open} onOpenChange={p.onOpenChange}>
      <SheetContent closeLabel={t('patientBanner.close')} aria-describedby={undefined} className="max-w-panel-chart">
        <SheetTitle>{t('studio.panel.label')}</SheetTitle>
        {tabs}
      </SheetContent>
    </Sheet>
  );
  return (
    <aside aria-label={t('studio.panel.label')} className="flex w-full max-w-panel-chart flex-col gap-3 rounded-xl border bg-card p-3 lg:sticky lg:top-[calc(var(--patient-strip-bottom,0px)+4rem)] [@media(max-height:480px)]:static">
      {tabs}
    </aside>
  );
}

/** Opens the drawer variant; it sits in the patient strip area. */
export function ChartDrawerButton({ open, onOpen }: { open: boolean; onOpen: () => void }) {
  const { t } = useI18n();
  return (
    <Button variant="outline" aria-haspopup="dialog" aria-expanded={open} onClick={onOpen}>
      <PanelRight aria-hidden className="size-4 rtl:-scale-x-100" />{t('studio.panel.chart')}
    </Button>
  );
}

type ChartState = ChartPanelProps['chart'] & { timeZone: string | undefined };

function Loaded({ data, loading, failed, children }: ChartPanelProps['chart'] & { children: (data: Patient360['chart']) => ReactNode }) {
  const { t } = useI18n();
  if (failed) return <p role="alert" className="text-sm font-bold text-danger-700">{t('patient360.loadFailed')}</p>;
  if (loading || !data) return <p role="status" className="text-sm">{t('patient360.loading')}</p>;
  return children(data);
}

function Part({ title, children }: { title: string; children: ReactNode }) {
  return <section className="flex flex-col gap-1"><h4 className="font-bold">{title}</h4>{children}</section>;
}

const Empty = ({ text }: { text: string }) => <p className="text-muted-foreground">{text}</p>;

/** The chart at a glance while writing: allergies, active medicines and problems, and the latest results. */
function ChartView({ timeZone, ...state }: ChartState) {
  const { t, formatDate } = useI18n();
  return (
    <Loaded {...state}>{chart => {
      const problems = chart.problems.filter(x => x.status === 'Active');
      const medicines = chart.medicines.filter(x => x.state === 'Active');
      const results = resultRows(chart);
      return (
        <div className="flex flex-col gap-3 text-sm">
          <Part title={t('patient360.allergies')}>
            <div><AllergyBlock allergy={bannerAllergyState(chart, s => t(`patient360.allergyStatus.${s}`))} /></div>
          </Part>
          <Part title={t('studio.chart.medicines')}>
            {medicines.length === 0 ? <Empty text={t('studio.chart.noneActive')} />
              : <ul className="list-disc ps-5">{medicines.map((x, i) => <li key={i}>{x.name}{x.dose && <span className="ms-1">{ltr(x.dose)}</span>}</li>)}</ul>}
          </Part>
          <Part title={t('studio.chart.problems')}>
            {problems.length === 0 ? <Empty text={t('studio.chart.noneActive')} />
              : <ul className="list-disc ps-5">{problems.map((x, i) => <li key={i}>{x.description}{x.icd10Code && <span className="ms-1 text-muted-foreground">{ltr(x.icd10Code)}</span>}</li>)}</ul>}
          </Part>
          <Part title={t('studio.chart.results')}>
            {results.length === 0 ? <Empty text={t('patient360.labs.empty')} />
              : <ul className="flex flex-col divide-y">{results.map(({ lab: l }) => (
                  <li key={l.code} className="flex flex-wrap items-baseline justify-between gap-x-3 py-1">
                    <span>{t(`patient360.code.${l.code}`)}<time dateTime={l.takenAt} className="ms-1 text-muted-foreground">{formatDate(new Date(l.takenAt), timeZone)}</time></span>
                    <span className="flex flex-col items-end">
                      <strong>{ltr(`${l.value} ${l.unit}`)}</strong>
                      {l.flag && l.range
                        ? <span className="inline-flex items-center gap-1.5 font-bold">
                            {l.flag === 'High' ? <ArrowUp aria-hidden className="size-4 shrink-0" /> : <ArrowDown aria-hidden className="size-4 shrink-0" />}
                            {t(`patient360.flag.${l.flag}`, { range: ltr(`${l.range} ${l.unit}`) })}
                          </span>
                        : l.range && <span className="text-muted-foreground">{t('patient360.labs.inRange')}</span>}
                    </span>
                  </li>))}</ul>}
          </Part>
        </div>
      );
    }}</Loaded>
  );
}

/** The ended visits; the open one is this visit. */
function PastVisits({ timeZone, ...state }: ChartState) {
  const { t, formatDate } = useI18n();
  return (
    <Loaded {...state}>{chart => {
      const past = chart.recentEncounters.filter(x => x.status === 'Ended');
      return past.length === 0 ? <p className="text-sm text-muted-foreground">{t('studio.chart.recent.none')}</p>
        : <ul aria-label={t('studio.panel.past')} className="list-disc ps-5 text-sm">{past.map(x => <li key={x.id}><time dateTime={x.startedAt}>{formatDate(new Date(x.startedAt), timeZone)}</time></li>)}</ul>;
    }}</Loaded>
  );
}
