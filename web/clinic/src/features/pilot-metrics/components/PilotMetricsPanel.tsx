import type { FormEventHandler, ReactNode } from 'react';
import { Controller, type UseFormReturn } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import { Button, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import { Field } from '../../settings/components/Field';
import { Section } from '../../settings/components/Section';
import { describedBy, fieldError } from '../../settings/components/fieldStyles';
import { pilotMetricIds, windowDays, type BaselineInput, type PilotMetricId, type PilotMetrics } from '../domain/pilotMetrics';

type Props = {
  state: 'loading' | 'ready' | 'forbidden' | 'error';
  metrics: PilotMetrics | null;
  gapSeconds: number | null;
  delivery: { percent: number | null; delivered: number; settled: number } | null;
  deliveryFailed: boolean;
  deliveryForbidden: boolean;
  form: UseFormReturn<BaselineInput>;
  saving: boolean;
  saveFailed: boolean;
  saved: boolean;
  onSubmit: FormEventHandler<HTMLFormElement>;
};

/** One number with what it means and the clinic's value before Medvya. */
function Tile({ label, value, detail, baseline }: { label: string; value: string; detail: ReactNode; baseline: string }) {
  return (
    <div className="flex flex-col gap-1 rounded-lg border p-4">
      <dt className="font-bold">{label}</dt>
      <dd className="text-3xl font-extrabold">{value}</dd>
      <dd className="text-sm">{detail}</dd>
      <dd className="text-sm text-muted-foreground">{baseline}</dd>
    </div>
  );
}

/** Pilot metrics (MDV-P1-041): aggregates only, never a patient. State is always written out, never colour alone. */
export default function PilotMetricsPanel(p: Props) {
  const { t, formatNumber, formatDay } = useI18n();
  const { errors } = p.form.formState;
  if (p.state === 'forbidden') return <p role="alert" className={fieldError}>{t('pilotMetrics.forbidden')}</p>;
  if (p.state === 'loading') return <p role="status">{t('pilotMetrics.loading')}</p>;
  if (p.state === 'error' || !p.metrics) return <p role="alert" className={fieldError}>{t('pilotMetrics.loadFailed')}</p>;
  const m = p.metrics;
  const unit = (metric: PilotMetricId, value: number) => t(`pilotMetrics.unit.${metric}`, { value: formatNumber(value) });
  const baseline = (metric: PilotMetricId) => {
    const b = m.baselines.find(x => x.metric === metric);
    return b ? t('pilotMetrics.baseline', { value: unit(metric, b.value), from: formatDay(b.fromDate), to: formatDay(b.toDate), method: b.method }) : t('pilotMetrics.noBaseline');
  };
  const none = t('pilotMetrics.none');
  const c = m.checkIn;

  return (
    <div className="flex flex-col gap-6">
      <Section title={t('pilotMetrics.title')} intro={t('pilotMetrics.intro', { days: formatNumber(windowDays) })}>
        <dl className="grid gap-4 md:grid-cols-2">
          <Tile label={t('pilotMetrics.checkIn')} value={c.returningMedianSeconds === null ? none : unit('CheckInSeconds', Math.round(c.returningMedianSeconds))}
            detail={<span className="flex flex-col gap-1">
              <span className="font-semibold">{p.gapSeconds === null ? t('pilotMetrics.checkInNoReturning', { target: formatNumber(c.targetSeconds) })
                : p.gapSeconds === 0 ? t('pilotMetrics.checkInMet', { target: formatNumber(c.targetSeconds) })
                : t('pilotMetrics.checkInGap', { gap: formatNumber(p.gapSeconds), target: formatNumber(c.targetSeconds) })}</span>
              <span>{t('pilotMetrics.checkInCounts', { returning: formatNumber(c.returningCount), all: formatNumber(c.count),
                median: c.medianSeconds === null ? none : unit('CheckInSeconds', Math.round(c.medianSeconds)) })}</span>
            </span>}
            baseline={baseline('CheckInSeconds')} />
          <Tile label={t('pilotMetrics.noShow')} value={m.noShows.percent === null ? none : unit('NoShowPercent', m.noShows.percent)}
            detail={t('pilotMetrics.noShowCounts', { noShows: formatNumber(m.noShows.noShows), total: formatNumber(m.noShows.noShows + m.noShows.attended) })}
            baseline={baseline('NoShowPercent')} />
          <Tile label={t('pilotMetrics.doubleBookings')} value={formatNumber(m.doubleBookingRefusals)} detail={t('pilotMetrics.doubleBookingsDetail')}
            baseline={baseline('DoubleBookings')} />
          <Tile label={t('pilotMetrics.delivery')} value={p.delivery?.percent == null ? none : unit('ReminderDeliveryPercent', p.delivery.percent)}
            detail={p.deliveryFailed ? t(p.deliveryForbidden ? 'pilotMetrics.deliveryForbidden' : 'pilotMetrics.deliveryFailed')
              : t('pilotMetrics.deliveryCounts', { delivered: formatNumber(p.delivery?.delivered ?? 0), settled: formatNumber(p.delivery?.settled ?? 0) })}
            baseline={baseline('ReminderDeliveryPercent')} />
        </dl>
      </Section>

      <Section title={t('pilotMetrics.recordTitle')} intro={t('pilotMetrics.recordIntro')}>
        <form noValidate onSubmit={p.onSubmit} className="grid gap-4 sm:grid-cols-2">
          <Field id="baseline-metric" label={t('pilotMetrics.metric')}>
            <Controller control={p.form.control} name="metric" render={({ field }) => (
              <Select value={field.value} onValueChange={field.onChange}>
                <SelectTrigger id="baseline-metric" className="h-11 w-full"><SelectValue /></SelectTrigger>
                <SelectContent>{pilotMetricIds.map(id => <SelectItem key={id} value={id}>{t(`pilotMetrics.metric.${id}`)}</SelectItem>)}</SelectContent>
              </Select>
            )} />
          </Field>
          <Field id="baseline-value" label={t('pilotMetrics.value')} error={errors.value ? t('pilotMetrics.error.value') : null}>
            <Input id="baseline-value" inputMode="decimal" autoComplete="off" className="h-11" {...describedBy('baseline-value', !!errors.value)} {...p.form.register('value')} />
          </Field>
          <Field id="baseline-method" label={t('pilotMetrics.method')} hint={t('pilotMetrics.methodHint')} className="sm:col-span-2"
            error={errors.method ? t('pilotMetrics.error.method') : null}>
            <Input id="baseline-method" autoComplete="off" maxLength={200} className="h-11" {...describedBy('baseline-method', !!errors.method, true)} {...p.form.register('method')} />
          </Field>
          <Field id="baseline-from" label={t('pilotMetrics.from')} error={errors.fromDate ? t('pilotMetrics.error.date') : null}>
            <Input id="baseline-from" type="date" className="h-11" {...describedBy('baseline-from', !!errors.fromDate)} {...p.form.register('fromDate')} />
          </Field>
          <Field id="baseline-to" label={t('pilotMetrics.to')} error={errors.toDate ? t('pilotMetrics.error.toDate') : null}>
            <Input id="baseline-to" type="date" className="h-11" {...describedBy('baseline-to', !!errors.toDate)} {...p.form.register('toDate')} />
          </Field>
          <div className="flex flex-wrap items-center gap-3 sm:col-span-2" aria-live="polite">
            <Button type="submit" className="min-h-11" loading={p.saving}>{t('pilotMetrics.record')}</Button>
            {p.saveFailed && <p role="alert" className={fieldError}>{t('pilotMetrics.saveFailed')}</p>}
            {p.saved && !p.saving && <p>{t('pilotMetrics.saved')}</p>}
          </div>
        </form>
      </Section>
    </div>
  );
}
