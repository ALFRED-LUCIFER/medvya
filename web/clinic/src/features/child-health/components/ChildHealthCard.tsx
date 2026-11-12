import type { ReactNode } from 'react';
import { Controller, type UseFormReturn } from 'react-hook-form';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, Input, Label, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Textarea } from '@medvya/ui';
import { CircleAlert, CircleCheck, CircleHelp, Clock, CircleX } from 'lucide-react';
import { describedBy } from '../../settings/components/fieldStyles';
import { answerers, answers, bandState, openDoses, type ChildHealth, type DoseDraft, type DoseState, type MilestoneAnswer, type MilestoneAnswerer, type MilestoneKey, type Problem } from '../domain/childHealth';

type Props = {
  view: ChildHealth | null; loading: boolean; failed: boolean; canRecord: boolean; online: boolean;
  form: UseFormReturn<DoseDraft>; problem: Problem | null; answeredBy: MilestoneAnswerer; recording: boolean; answering: boolean;
  onRecord: () => void; onAnsweredBy: (a: MilestoneAnswerer) => void; onAnswer: (milestone: MilestoneKey, value: MilestoneAnswer) => void;
};

// Each state has its own icon and word: never colour alone.
const stateIcon: Record<DoseState, ReactNode> = {
  Given: <CircleCheck aria-hidden className="size-4 shrink-0" />,
  Overdue: <CircleAlert aria-hidden className="size-4 shrink-0" />,
  DueNow: <Clock aria-hidden className="size-4 shrink-0" />,
  Upcoming: <Clock aria-hidden className="size-4 shrink-0 opacity-60" />,
};
const answerIcon: Record<MilestoneAnswer, ReactNode> = {
  Yes: <CircleCheck aria-hidden className="size-4 shrink-0" />, No: <CircleX aria-hidden className="size-4 shrink-0" />, Unsure: <CircleHelp aria-hidden className="size-4 shrink-0" />,
};

/**
 * Immunisations against the country schedule and the milestones of the child's age band (MDV-P2-036, E-01): presentational, so the one-page
 * chart (MDV-P2-033) can reuse it. Shown only for a child. Recording is for a doctor or nurse; the server decides every write.
 */
export default function ChildHealthCard(p: Props) {
  const { t } = useI18n();
  if (p.loading) return <p role="status">{t('childHealth.loading')}</p>;
  if (p.failed || !p.view) return p.failed ? <p role="alert">{t('childHealth.loadFailed')}</p> : null;
  if (!p.view.child) return null;
  const v = p.view;
  return (
    <div className="grid gap-4 md:grid-cols-2">
      <section aria-labelledby="imm-title" className="flex flex-col gap-3 rounded-xl border bg-card p-4">
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <h2 id="imm-title" className="text-base font-extrabold">{t('childHealth.immunisations')}</h2>
          <span className="text-sm text-muted-foreground">{t('childHealth.schedule', { name: ltr(v.schedule) })}</span>
        </div>
        {!v.signedOff && <p className="text-sm text-muted-foreground">{t('childHealth.scheduleDraft')}</p>}
        <ul className="flex flex-col gap-3">
          {v.bands.map(b => {
            const state = bandState(b);
            return (
              <li key={b.key} className="flex flex-col gap-1 border-t pt-2">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <strong>{t(`childHealth.band.${b.key}`)}</strong>
                  <span className="inline-flex items-center gap-1.5 text-sm font-bold">{stateIcon[state]}{t(`childHealth.state.${state}`)}</span>
                </div>
                <ul className="flex flex-col gap-0.5 text-sm">
                  {b.doses.map(d => (
                    <li key={`${d.vaccine}${d.dose}`} className="flex flex-wrap gap-x-2">
                      <span className="font-bold">{t('childHealth.dose', { vaccine: ltr(t(`childHealth.vaccine.${d.vaccine}`)), dose: d.dose })}</span>
                      <span>{t(`childHealth.state.${d.state}`)}</span>
                      {d.givenOn && <span>{t('childHealth.givenOn')}{' '}<Day iso={d.givenOn} />{d.batch && <>{' · '}{t('childHealth.batch', { batch: ltr(d.batch) })}</>}{d.by && <>{' · '}{d.by}</>}</span>}
                      {d.deferReason && <span>{t('childHealth.deferred', { reason: d.deferReason })}{d.deferredOn && <>{' · '}<Day iso={d.deferredOn} /></>}</span>}
                    </li>
                  ))}
                </ul>
              </li>
            );
          })}
        </ul>
        {p.canRecord && <DoseForm {...p} view={v} />}
        {p.problem && <p role="alert" className="font-bold text-danger-700">{t(`childHealth.error.${p.problem}`)}</p>}
      </section>
      <section aria-labelledby="miles-title" className="flex flex-col gap-3 rounded-xl border bg-card p-4">
        <h2 id="miles-title" className="text-base font-extrabold">{v.milestones ? t('childHealth.milestonesFor', { band: t(`childHealth.milestoneBand.${v.milestones.key}`) }) : t('childHealth.milestones')}</h2>
        {!v.milestones ? <p className="text-muted-foreground">{t('childHealth.milestones.none')}</p> : (
          <>
            {!v.milestones.signedOff && <p className="text-sm text-muted-foreground">{t('childHealth.milestonesDraft')}</p>}
            {p.canRecord && (
              <div className="flex flex-col gap-1">
                <Label htmlFor="answered-by">{t('childHealth.answeredBy')}</Label>
                <Select value={p.answeredBy} onValueChange={a => p.onAnsweredBy(a as MilestoneAnswerer)}>
                  <SelectTrigger id="answered-by" className="h-11 w-full sm:w-60"><SelectValue /></SelectTrigger>
                  <SelectContent>{answerers.map(a => <SelectItem key={a} value={a}>{t(`childHealth.answerer.${a}`)}</SelectItem>)}</SelectContent>
                </Select>
              </div>
            )}
            <ul className="flex flex-col gap-3">
              {v.milestones.milestones.map(m => {
                const name = t(`childHealth.milestone.${m.key}`);
                return (
                  <li key={m.key} className="flex flex-col gap-1 border-t pt-2">
                    <span>{name}</span>
                    <span className="inline-flex items-center gap-1.5 text-sm font-bold">
                      {m.answer ? <>{answerIcon[m.answer]}{t(`childHealth.answer.${m.answer}`)}{m.answeredBy && <>{' · '}{t(`childHealth.answerer.${m.answeredBy}`)}</>}</> : t('childHealth.notAnswered')}
                    </span>
                    {p.canRecord && (
                      <div role="group" aria-label={t('childHealth.answerFor', { name })} className="flex flex-wrap gap-2">
                        {answers.map(a => (
                          <Button key={a} type="button" size="sm" variant={m.answer === a ? 'selected' : 'outline'} className="min-h-11" aria-pressed={m.answer === a}
                            disabled={!p.online || p.answering} onClick={() => p.onAnswer(m.key, a)}>{t(`childHealth.answer.${a}`)}</Button>
                        ))}
                      </div>
                    )}
                  </li>
                );
              })}
            </ul>
          </>
        )}
      </section>
    </div>
  );
}

/** Record a dose of the schedule as given (day, batch, who gave it) or deferred (reason). */
function DoseForm(p: Props & { view: ChildHealth }) {
  const { t } = useI18n();
  const open = openDoses(p.view.bands);
  const outcome = p.form.watch('outcome');
  const errors = p.form.formState.errors;
  if (open.length === 0) return null;
  return (
    <form noValidate onSubmit={e => { e.preventDefault(); p.onRecord(); }} className="flex flex-col gap-3 border-t pt-3" aria-labelledby="dose-form-title">
      <h3 id="dose-form-title" className="font-extrabold">{t('childHealth.record')}</h3>
      <div className="grid gap-3 sm:grid-cols-2">
        <div className="flex flex-col gap-1">
          <Label htmlFor="dose-pick">{t('childHealth.record.dose')}</Label>
          <Controller control={p.form.control} name="dose" render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <SelectTrigger id="dose-pick" className="h-11 w-full" {...describedBy('dose-pick', !!errors.dose, false)}><SelectValue placeholder={t('childHealth.record.pick')} /></SelectTrigger>
              <SelectContent>{open.map(d => <SelectItem key={d.key} value={d.key}>{t('childHealth.dose', { vaccine: ltr(t(`childHealth.vaccine.${d.vaccine}`)), dose: d.dose })}</SelectItem>)}</SelectContent>
            </Select>
          )} />
          {errors.dose && <p id="dose-pick-error" className="text-sm font-bold text-danger-700">{t('childHealth.record.doseRequired')}</p>}
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="dose-outcome">{t('childHealth.record.outcome')}</Label>
          <Controller control={p.form.control} name="outcome" render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange}>
              <SelectTrigger id="dose-outcome" className="h-11 w-full"><SelectValue /></SelectTrigger>
              <SelectContent>{(['Given', 'Deferred'] as const).map(o => <SelectItem key={o} value={o}>{t(`childHealth.outcome.${o}`)}</SelectItem>)}</SelectContent>
            </Select>
          )} />
        </div>
        {outcome === 'Given' ? <>
          <div className="flex flex-col gap-1">
            <Label htmlFor="dose-given-on">{t('childHealth.record.givenOn')}</Label>
            <Input id="dose-given-on" type="date" className="h-11" {...describedBy('dose-given-on', !!errors.givenOn, false)} {...p.form.register('givenOn')} />
            {errors.givenOn && <p id="dose-given-on-error" className="text-sm font-bold text-danger-700">{t('childHealth.record.givenOnRequired')}</p>}
          </div>
          <div className="flex flex-col gap-1">
            <Label htmlFor="dose-batch">{t('childHealth.record.batch')}</Label>
            <Input id="dose-batch" autoComplete="off" maxLength={50} className="h-11" {...p.form.register('batch')} />
          </div>
          <div className="flex flex-col gap-1 sm:col-span-2">
            <Label htmlFor="dose-given-by">{t('childHealth.record.givenBy')}</Label>
            <Input id="dose-given-by" autoComplete="off" maxLength={100} className="h-11" {...describedBy('dose-given-by', false, true)} {...p.form.register('givenBy')} />
            <p id="dose-given-by-hint" className="text-sm text-muted-foreground">{t('childHealth.record.givenByHint')}</p>
          </div>
        </> : (
          <div className="flex flex-col gap-1 sm:col-span-2">
            <Label htmlFor="dose-reason">{t('childHealth.record.reason')}</Label>
            <Textarea id="dose-reason" maxLength={500} {...describedBy('dose-reason', !!errors.reason, false)} {...p.form.register('reason')} />
            {errors.reason && <p id="dose-reason-error" className="text-sm font-bold text-danger-700">{t('childHealth.record.reasonRequired')}</p>}
          </div>
        )}
      </div>
      <div><Button type="submit" className="min-h-11" loading={p.recording} disabled={!p.online}>{t('childHealth.record.submit')}</Button></div>
    </form>
  );
}

/** A schedule day (no time of day): shown as the calendar day it is, whatever the device's zone. */
function Day({ iso }: { iso: string }) {
  const { formatDate } = useI18n();
  return <time dateTime={iso}>{formatDate(new Date(`${iso}T00:00:00Z`), 'UTC')}</time>;
}
