import type { UseFormReturn } from 'react-hook-form';
import { Controller } from 'react-hook-form';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, Input, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Table, TableBody, TableCaption, TableCell, TableHead, TableHeader, TableRow } from '@medvya/ui';
import { CircleAlert, TrendingDown, TrendingUp } from 'lucide-react';
import { describedBy } from '../../settings/components/fieldStyles';
import {
  centileWord, growthBox, growthChart, indicators, latestOf, positions, type Growth, type GrowthAlert, type GrowthDraft, type GrowthIndicator, type GrowthProblem, type GrowthSeries,
} from '../domain/growth';

type Props = {
  view: Growth | null; loading: boolean; failed: boolean; canRecord: boolean; online: boolean;
  form: UseFormReturn<GrowthDraft>; problem: GrowthProblem | null; indicator: GrowthIndicator; recording: boolean;
  onIndicator: (i: GrowthIndicator) => void; onRecord: () => void;
};

const alertIcon: Record<GrowthAlert, typeof CircleAlert> = { BelowThird: CircleAlert, CrossedDown: TrendingDown, CrossedUp: TrendingUp };

/**
 * A child's growth (MDV-P2-035, E-01): WHO centile lines with the child's points, the centile in words, the flags and the weight velocity,
 * the measurements and a form to record them. Presentational, so the one-page chart (MDV-P2-033) can reuse it. The server decides every number.
 */
export default function GrowthCard(p: Props) {
  const { t, formatNumber } = useI18n();
  if (p.loading) return <p role="status">{t('childHealth.growth.loading')}</p>;
  if (p.failed || !p.view) return p.failed ? <p role="alert">{t('childHealth.growth.loadFailed')}</p> : null;
  if (!p.view.child) return null;
  const v = p.view;
  const series = v.series.find(s => s.indicator === p.indicator) ?? null;
  return (
    <section aria-labelledby="growth-title" className="flex flex-col gap-3 rounded-xl border bg-card p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="growth-title" className="text-base font-extrabold">{t('childHealth.growth.title')}</h2>
        {!v.noCentiles && (
          <div className="flex flex-wrap gap-2">
            {indicators.map(i => (
              <Button key={i} type="button" variant={i === p.indicator ? 'navy' : 'outline'} className="min-h-11" aria-pressed={i === p.indicator} onClick={() => p.onIndicator(i)}>
                {t(`childHealth.growth.indicator.${i}`)}
              </Button>
            ))}
          </div>
        )}
      </div>
      {!v.signedOff && <p className="text-sm text-muted-foreground">{t('childHealth.growth.draft')}</p>}
      {v.noCentiles ? <p className="font-bold">{t(`childHealth.growth.noCentiles.${v.noCentiles}`)}</p> : series && <Chart series={series} />}
      {v.velocity && (
        <p>
          <strong>{t('childHealth.growth.velocity')}</strong>{' '}
          {t('childHealth.growth.velocityValue', { change: ltr(`${v.velocity.changeKg > 0 ? '+' : ''}${formatNumber(v.velocity.changeKg)}`), days: v.velocity.days })}
        </p>
      )}
      <Measurements view={v} />
      {p.canRecord && <GrowthForm {...p} />}
      {p.problem && <p role="alert" className="font-bold text-danger-700">{t(`childHealth.growth.error.${p.problem}`)}</p>}
    </section>
  );
}

function Chart({ series }: { series: GrowthSeries }) {
  const { t, formatNumber, formatDate } = useI18n();
  const chart = growthChart(series);
  const name = t(`childHealth.growth.indicator.${series.indicator}`);
  const last = latestOf(series);
  const day = (iso: string) => formatDate(new Date(`${iso}T00:00:00Z`), 'UTC');
  const centile = last ? t('childHealth.growth.centile', { centile: centileWord(last.latest.centile) }) : '';
  const trend = !last ? '' : last.before
    ? t(`childHealth.growth.trend.${last.trend === 'single' ? 'same' : last.trend}`, { centile: centileWord(last.before.centile), date: day(last.before.takenOn) })
    : t('childHealth.growth.trend.single');
  // The text alternative names the latest centile and the trend: the chart says nothing a screen reader does not hear.
  const alt = last ? t('childHealth.growth.alt', { name, centile, date: day(last.latest.takenOn), trend }) : t('childHealth.growth.altNone', { name });
  return (
    <div className="flex flex-col gap-2">
      {/* A chart reads left to right (age) in every language; only the words around it turn for Arabic. */}
      {chart && (
        <svg role="img" aria-label={alt} style={{ direction: 'ltr' }} viewBox={`0 0 ${growthBox.width} ${growthBox.height}`} className="h-auto w-full max-w-2xl" fill="none">
          {chart.bands.map((d, i) => <path key={d} d={d} className={i === 0 ? 'fill-surface' : 'fill-brand-50'} />)}
          {chart.lines.map(l => (
            <g key={l.centile} className={l.centile === 50 ? 'text-brand-700' : 'text-muted-foreground'}>
              <path d={l.d} stroke="currentColor" strokeWidth={l.centile === 50 ? 1.5 : 1} strokeDasharray={l.centile === 50 ? undefined : '4 3'} />
              <text x={l.end.x + 3} y={l.end.y + 3} fontSize="10" fill="currentColor">{t(`childHealth.growth.line.${l.centile}`)}</text>
            </g>
          ))}
          <g className="text-danger-700">
            <path d={chart.childPath} stroke="currentColor" strokeWidth="2.5" strokeLinejoin="round" strokeLinecap="round" />
            {chart.child.map((pt, i) => <circle key={i} cx={pt.x} cy={pt.y} r="3.5" className="fill-card" stroke="currentColor" strokeWidth="2" />)}
          </g>
          <g className="text-muted-foreground" fontSize="10" fill="currentColor">
            {chart.xTicks.map(x => <text key={x.years} x={x.x} y={growthBox.bottom + 16} textAnchor="middle">{t('childHealth.growth.years', { years: x.years })}</text>)}
            {chart.yTicks.map(y => <text key={y.value} x={growthBox.left - 6} y={y.y + 3} textAnchor="end">{formatNumber(y.value)}</text>)}
          </g>
        </svg>
      )}
      {last ? (
        <p>
          <strong>{t('childHealth.growth.latest', { name })}</strong>{' '}
          {t(`childHealth.growth.value.${series.indicator}`, { value: formatNumber(last.latest.value) })}{' · '}{centile}{' '}
          {t('childHealth.growth.z', { z: ltr(formatNumber(last.latest.z)) })}{' · '}{trend}
        </p>
      ) : <p className="text-muted-foreground">{t('childHealth.growth.noPoints', { name })}</p>}
      {series.flags.length > 0 && (
        <ul className="flex flex-col gap-1">
          {series.flags.map(f => {
            const Icon = alertIcon[f];
            return <li key={f} className="inline-flex items-center gap-1.5 font-bold text-danger-700"><Icon aria-hidden className="size-4 shrink-0" />{t(`childHealth.growth.alert.${f}`)}</li>;
          })}
        </ul>
      )}
    </div>
  );
}

function Measurements({ view }: { view: Growth }) {
  const { t, formatNumber, formatDate } = useI18n();
  if (view.measurements.length === 0) return <p className="text-muted-foreground">{t('childHealth.growth.none')}</p>;
  const n = (x: number | null) => (x === null ? '' : formatNumber(x));
  return (
    <Table>
      <TableCaption className="sr-only">{t('childHealth.growth.table')}</TableCaption>
      <TableHeader>
        <TableRow>
          <TableHead scope="col">{t('childHealth.growth.col.date')}</TableHead>
          <TableHead scope="col">{t('childHealth.growth.col.weight')}</TableHead>
          <TableHead scope="col">{t('childHealth.growth.col.length')}</TableHead>
          <TableHead scope="col">{t('childHealth.growth.col.head')}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {[...view.measurements].reverse().map(m => (
          <TableRow key={m.takenOn}>
            <TableCell><time dateTime={m.takenOn}>{formatDate(new Date(`${m.takenOn}T00:00:00Z`), 'UTC')}</time></TableCell>
            <TableCell>{n(m.weightKg)}</TableCell>
            <TableCell>{m.lengthCm !== null && <>{n(m.lengthCm)}{' · '}{t(m.position ? `childHealth.growth.position.${m.position}` : 'childHealth.growth.position.unstated')}</>}</TableCell>
            <TableCell>{n(m.headCm)}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

/** Record the day's weight, length or height (lying or standing) and head circumference; any one of them is enough. */
function GrowthForm(p: Props) {
  const { t } = useI18n();
  const errors = p.form.formState.errors;
  const field = (name: 'weightKg' | 'lengthCm' | 'headCm', id: string) => (
    <div className="flex flex-col gap-1">
      <Label htmlFor={id}>{t(`childHealth.growth.form.${name}`)}</Label>
      <Input id={id} inputMode="decimal" autoComplete="off" className="h-11" {...describedBy(id, !!errors[name], false)} {...p.form.register(name)} />
      {errors[name] && <p id={`${id}-error`} className="text-sm font-bold text-danger-700">{t(errors[name].message === 'required' ? 'childHealth.growth.form.oneRequired' : 'childHealth.growth.form.number')}</p>}
    </div>
  );
  return (
    <form noValidate onSubmit={e => { e.preventDefault(); p.onRecord(); }} className="flex flex-col gap-3 border-t pt-3" aria-labelledby="growth-form-title">
      <h3 id="growth-form-title" className="font-extrabold">{t('childHealth.growth.form.title')}</h3>
      <div className="grid gap-3 sm:grid-cols-2">
        <div className="flex flex-col gap-1">
          <Label htmlFor="growth-on">{t('childHealth.growth.form.takenOn')}</Label>
          <Input id="growth-on" type="date" className="h-11" {...describedBy('growth-on', !!errors.takenOn, false)} {...p.form.register('takenOn')} />
          {errors.takenOn && <p id="growth-on-error" className="text-sm font-bold text-danger-700">{t('childHealth.growth.form.takenOnRequired')}</p>}
        </div>
        {field('weightKg', 'growth-weight')}
        {field('lengthCm', 'growth-length')}
        <div className="flex flex-col gap-1">
          <Label htmlFor="growth-position">{t('childHealth.growth.form.position')}</Label>
          <Controller control={p.form.control} name="position" render={({ field: f }) => (
            <Select value={f.value} onValueChange={f.onChange}>
              <SelectTrigger id="growth-position" className="h-11 w-full" {...describedBy('growth-position', !!errors.position, false)}><SelectValue placeholder={t('childHealth.growth.form.positionPick')} /></SelectTrigger>
              <SelectContent>{positions.map(o => <SelectItem key={o} value={o}>{t(`childHealth.growth.position.${o}`)}</SelectItem>)}</SelectContent>
            </Select>
          )} />
          {errors.position && <p id="growth-position-error" className="text-sm font-bold text-danger-700">{t('childHealth.growth.form.positionRequired')}</p>}
        </div>
        {field('headCm', 'growth-head')}
      </div>
      <div><Button type="submit" className="min-h-11" loading={p.recording} disabled={!p.online}>{t('childHealth.growth.form.submit')}</Button></div>
    </form>
  );
}
