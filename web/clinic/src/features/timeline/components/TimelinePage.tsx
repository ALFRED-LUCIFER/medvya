import { Link } from 'react-router';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, buttonVariants, ProvenanceTag, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Table, TableBody, TableCaption, TableCell, TableHead, TableHeader, TableRow } from '@medvya/ui';
import type { ChartSource, ClinicalRefusal, Patient360 } from '../../patient360/domain/chart';
import { pathOf, provenanceOf, sparkBox } from '../../patient360/domain/chart';
import { PatientBanner } from '../../patient360/components/Patient360Page';
import { chartLines, hasFilters, timelineKinds, timelineSources, trendChoices, type TimelineEntry, type TimelineFilters, type TimelineKind, type TrendChoice, type TrendSeries } from '../domain/timeline';

type Props = {
  patient: Patient360 | null; filters: TimelineFilters; entries: TimelineEntry[]; timeZone: string;
  loading: boolean; notFound: boolean; refusal: ClinicalRefusal | null; failed: boolean;
  canLoadMore: boolean; loadingMore: boolean; loadMoreFailed: boolean;
  choice: TrendChoice; series: TrendSeries | null; trendsLoading: boolean; trendsFailed: boolean;
  backPath: string;
  onKind: (k: TimelineKind) => void; onAllKinds: () => void; onSource: (s: ChartSource | null) => void; onClear: () => void; onChoice: (c: TrendChoice) => void; onMore: () => void;
};

/**
 * The longitudinal timeline and trends (MDV-P2-005, E-02): one newest-first list across encounters, results, medicines and documents, each with
 * its date, what it is, who or what it came from and a source tag; filters that combine; an older-entries button that adds below what is on
 * screen; and the trends as a chart with the same numbers in a table.
 */
export default function TimelinePage(p: Props) {
  const { t } = useI18n();
  if (p.refusal) return <Message title={t('patient360.refused.title')} body={t(p.refusal.canRequest ? 'consult.refused.ask' : 'patient360.refused.role')} backPath={p.backPath} />;
  if (p.notFound || p.failed) return <Message title={t(p.notFound ? 'patient360.notFound' : 'timeline.loadFailed')} backPath={p.backPath} />;
  return (
    <div className="flex flex-col gap-4">
      {p.patient && <PatientBanner data={p.patient} />}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-xl font-extrabold">{t('timeline.title')}</h1>
        <Link to={p.backPath} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('timeline.backToChart')}</Link>
      </div>
      <Filters {...p} />
      <div className="grid gap-4 lg:grid-cols-[3fr_2fr]">
        <section aria-label={t('timeline.list')} className="flex flex-col gap-3 rounded-xl border bg-card p-4">
          {p.loading ? <p role="status">{t('timeline.loading')}</p>
            : p.entries.length === 0 ? <Empty {...p} />
            : <ol className="flex flex-col gap-3">{p.entries.map(e => <Entry key={e.id} entry={e} zone={p.timeZone} />)}</ol>}
          {p.canLoadMore && (
            <Button variant="outline" className="min-h-11 self-start" loading={p.loadingMore} onClick={p.onMore}>{t('timeline.loadEarlier')}</Button>
          )}
          {p.loadMoreFailed && <p role="alert" className="font-bold text-danger-700">{t('timeline.loadMoreFailed')}</p>}
          {!p.loading && !p.canLoadMore && p.entries.length > 0 && <p role="status" className="text-sm text-muted-foreground">{t('timeline.end')}</p>}
        </section>
        <Trends {...p} />
      </div>
    </div>
  );
}

function Filters(p: Props) {
  const { t } = useI18n();
  const all = p.filters.kinds.length === 0;
  return (
    <section aria-label={t('timeline.filters')} className="flex flex-wrap items-center gap-2">
      <Button variant={all ? 'navy' : 'outline'} className="min-h-11" aria-pressed={all} onClick={p.onAllKinds}>{t('timeline.kind.All')}</Button>
      {timelineKinds.map(k => {
        const on = p.filters.kinds.includes(k);
        return <Button key={k} variant={on ? 'navy' : 'outline'} className="min-h-11" aria-pressed={on} onClick={() => p.onKind(k)}>{t(`timeline.kind.${k}`)}</Button>;
      })}
      <Select value={p.filters.source ?? 'all'} onValueChange={v => p.onSource(v === 'all' ? null : (v as ChartSource))}>
        <SelectTrigger className="h-11 w-56" aria-label={t('timeline.source')}><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem value="all">{t('timeline.source.all')}</SelectItem>
          {timelineSources.map(s => <SelectItem key={s} value={s}>{t(`timeline.source.${s}`)}</SelectItem>)}
        </SelectContent>
      </Select>
      {hasFilters(p.filters) && <Button variant="ghost" className="min-h-11" onClick={p.onClear}>{t('timeline.clear')}</Button>}
    </section>
  );
}

function Empty(p: Props) {
  const { t } = useI18n();
  return (
    <div className="flex flex-col items-start gap-2">
      <p className="font-bold">{t(hasFilters(p.filters) ? 'timeline.empty.filtered' : 'timeline.empty')}</p>
      {hasFilters(p.filters) && <Button variant="outline" className="min-h-11" onClick={p.onClear}>{t('timeline.clear')}</Button>}
    </div>
  );
}

export function Entry({ entry: e, zone }: { entry: TimelineEntry; zone: string }) {
  const { t, formatDate, formatTime } = useI18n();
  const when = new Date(e.at);
  const title = e.title ?? t(`timeline.code.${e.code}`);
  // A document's detail is the date written on it; a result's, its value; a medicine's, its dose.
  const detail = e.kind === 'Document' && e.detail ? t('documents.documentDate', { date: formatDate(new Date(`${e.detail}T12:00:00Z`), 'UTC') }) : e.detail && ltr(e.detail);
  const subtitle = [t(`timeline.kind.${e.kind}`), detail, e.author].filter(Boolean).join(' · ');
  return (
    <li className="flex flex-wrap items-start justify-between gap-x-4 gap-y-1 border-b pb-3 last:border-b-0">
      <div className="flex flex-col">
        <time dateTime={e.at} className="text-sm text-muted-foreground">{`${formatDate(when, zone)} ${formatTime(when, zone)}`}</time>
        <strong>{title}</strong>
        <span className="text-sm">{subtitle}</span>
      </div>
      <ProvenanceTag source={provenanceOf[e.source]} />
    </li>
  );
}

function Trends(p: Props) {
  const { t, formatDate } = useI18n();
  const lines = p.series ? chartLines(p.series) : null;
  const name = t(`patient360.trend.${p.choice}`);
  return (
    <section aria-label={t('patient360.trends')} className="flex flex-col gap-3 rounded-xl border bg-card p-4">
      <h2 className="text-base font-extrabold">{t('patient360.trends')}</h2>
      <div className="flex flex-wrap gap-2">
        {trendChoices.map(c => (
          <Button key={c} variant={c === p.choice ? 'navy' : 'outline'} className="min-h-11" aria-pressed={c === p.choice} onClick={() => p.onChoice(c)}>{t(`patient360.trend.${c}`)}</Button>
        ))}
      </div>
      {p.trendsLoading ? <p role="status">{t('timeline.loading')}</p>
        : p.trendsFailed ? <p role="alert" className="font-bold text-danger-700">{t('timeline.trendsFailed')}</p>
        : !p.series || !lines ? <p className="text-muted-foreground">{t('timeline.trends.none', { name })}</p>
        : (
          <>
            <svg role="img" aria-label={t('timeline.trends.alt', { name, count: p.series.rows.length, unit: p.series.unit })} viewBox={`0 0 ${sparkBox.width} ${sparkBox.height}`} className="h-32 w-full text-brand-700" fill="none">
              {lines.second.length > 0 && <path d={pathOf(lines.second)} stroke="currentColor" strokeWidth="1" strokeDasharray="3 2" />}
              <path d={pathOf(lines.main)} stroke="currentColor" strokeWidth="1.5" />
              {lines.main.map((pt, i) => <circle key={i} cx={pt.x} cy={pt.y} r="1.6" fill="currentColor" />)}
            </svg>
            <Table>
              <TableCaption className="sr-only">{t('timeline.trends.table', { name, unit: p.series.unit })}</TableCaption>
              <TableHeader>
                <TableRow>
                  <TableHead scope="col">{t('timeline.trends.date')}</TableHead>
                  <TableHead scope="col">{t('timeline.trends.value', { unit: p.series.unit })}</TableHead>
                  <TableHead scope="col">{t('timeline.source')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {p.series.rows.map(r => (
                  <TableRow key={r.takenAt}>
                    <TableCell><time dateTime={r.takenAt}>{formatDate(new Date(r.takenAt), p.timeZone)}</time></TableCell>
                    <TableCell>{ltr(r.value)}</TableCell>
                    <TableCell><ProvenanceTag source={provenanceOf[r.source]} /></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </>
        )}
    </section>
  );
}

function Message({ title, body, backPath }: { title: string; body?: string; backPath: string }) {
  const { t } = useI18n();
  return (
    <div role="alert" className="flex flex-col items-start gap-3 rounded-xl border bg-card p-6">
      <h1 className="text-lg font-extrabold">{title}</h1>
      {body && <p>{body}</p>}
      <Link to={backPath} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('timeline.backToChart')}</Link>
    </div>
  );
}
