import { useI18n } from '@medvya/i18n';
import { Button, ErrorSummary, Field, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import { CalendarClock, CircleAlert, CircleCheck, CircleDashed, HeartPulse, TriangleAlert } from 'lucide-react';
import type { FormEventHandler, ReactNode } from 'react';
import type { FieldErrors, UseFormReturn } from 'react-hook-form';
import type { ContactState, OpenPregnancy, PregnancyStatus } from '../domain/chart';
import { gestation, gpal, outcomes, type CloseForm, type PregnancyForm as Form, type PregnancyProblem, type ReadingsForm, type StartForm } from '../domain/pregnancy';

type Props = {
  status: PregnancyStatus; timeZone: string; canManage: boolean; canRecord: boolean; form: Form | null;
  /** Outside the core age band the card is folded under its heading until opened (UX-09). */
  folded: boolean;
  startForm: UseFormReturn<StartForm>; closeForm: UseFormReturn<CloseForm>; readingsForm: UseFormReturn<ReadingsForm>;
  saving: boolean; problem: PregnancyProblem | null;
  onOpen: (f: Form) => void; onCancel: () => void; onStart: FormEventHandler; onClose: FormEventHandler; onReadings: FormEventHandler;
  onCounselled: () => void; onRuledOut: () => void;
};

const stateIcon = { Done: CircleCheck, Today: CalendarClock, ToCome: CircleDashed, Missed: CircleAlert } as const satisfies Record<ContactState, unknown>;

/**
 * The pregnancy card (MDV-P2-034, E-01 "preg", "anc", "danger"). Presentational: the one-page chart (MDV-P2-033) reuses it. Every state is
 * said in words beside its icon (never colour alone); the trimester bar has a text alternative.
 */
export default function PregnancyCard(p: Props) {
  const { t } = useI18n();
  const open = p.status.open;
  const title = <h2 className="inline-flex items-center gap-2 text-base font-extrabold"><HeartPulse aria-hidden className="size-5" />{t('pregnancy.title')}</h2>;
  const body = <>
    {p.problem && <p role="alert" className="font-bold">{t(`pregnancy.problem.${p.problem}`)}</p>}
    {open ? <Open p={p} open={open} /> : <None p={p} />}
  </>;
  return (
    <section aria-label={t('pregnancy.title')} className="flex flex-col gap-4 rounded-xl border bg-card p-4">
      {p.folded ? (
        // Kept open while a form is in use, so a save or a refusal is never hidden.
        <details open={p.form !== null || undefined}>
          <summary className="flex min-h-11 cursor-pointer items-center">{title}</summary>
          <div className="mt-4 flex flex-col gap-4">{body}</div>
        </details>
      ) : <>
        <div className="flex flex-wrap items-center justify-between gap-2">
          {title}
          {open && <span className="text-sm text-muted-foreground">{t(`pregnancy.dating.${open.datingReason}`)}</span>}
        </div>
        {body}
      </>}
    </section>
  );
}

function None({ p }: { p: Props }) {
  const { t, formatDate, formatTime } = useI18n();
  const ruled = p.status.ruledOut;
  return (
    <>
      <p>{ruled
        ? t('pregnancy.ruledOut', { author: ruled.recordedBy ?? t('pregnancy.someone'), date: formatDate(new Date(ruled.recordedAt), p.timeZone), time: formatTime(new Date(ruled.recordedAt), p.timeZone) })
        : t('pregnancy.none')}</p>
      {p.form === 'start' ? <StartFields p={p} /> : (
        <div className="flex flex-wrap gap-2">
          {/* Secondary, never the chart's one primary action (UX-09). */}
          {p.canManage && <Button type="button" variant="outline" onClick={() => p.onOpen('start')}>{t('pregnancy.start')}</Button>}
          {p.canRecord && !ruled && <Button type="button" variant="outline" disabled={p.saving} onClick={p.onRuledOut}>{t('pregnancy.ruleOut')}</Button>}
        </div>
      )}
    </>
  );
}

function Open({ p, open }: { p: Props; open: OpenPregnancy }) {
  const { t, formatDate, formatCalendarDate } = useI18n();
  const g = gestation(open);
  const day = formatCalendarDate;
  const ga = t('pregnancy.ga', { weeks: g.weeks, days: g.days });
  return (
    <>
      <dl className="grid gap-3 sm:grid-cols-3">
        <Stat label={t('pregnancy.gestation')} value={ga} note={t(`pregnancy.trimester.${g.trimester}`)} />
        <Stat label={t('pregnancy.dueDate')} value={day(open.dueDate)}
          note={[open.lmp && t('pregnancy.lmp', { date: day(open.lmp) }), open.previousDueDate && t('pregnancy.previousDueDate', { date: day(open.previousDueDate) })].filter(Boolean).join(' · ')} />
        <Stat label={t('pregnancy.obstetric')} value={<span dir="ltr">{gpal(open)}</span>} note={t('pregnancy.gpal')} />
      </dl>
      <div className="flex flex-col gap-1.5">
        <div aria-hidden className="flex justify-between text-xs font-bold text-muted-foreground"><span>{t('pregnancy.trimester.1')}</span><span>{t('pregnancy.trimester.2')}</span><span>{t('pregnancy.trimester.3')}</span></div>
        <div role="img" aria-label={t('pregnancy.bar', { ga, trimester: t(`pregnancy.trimester.${g.trimester}`) })} className="relative h-3 overflow-hidden rounded-full bg-muted">
          <div className="absolute inset-y-0 start-0 rounded-full bg-primary" style={{ width: `${g.percent}%` }} />
        </div>
      </div>
      <div className="flex flex-col gap-2">
        <h3 className="font-bold">{t('pregnancy.contacts')}</h3>
        <ol className="grid grid-cols-2 gap-2 sm:grid-cols-5">
          {open.contacts.map(c => {
            const Icon = stateIcon[c.state];
            return (
              <li key={c.week} className={`flex items-center gap-1.5 rounded-md border px-2 py-1 text-sm ${c.state === 'Today' ? 'border-foreground font-bold' : ''}`}>
                <Icon aria-hidden className="size-4 shrink-0" />
                <span>{t('pregnancy.contact', { week: c.week, state: t(`pregnancy.contactState.${c.state}`) })}</span>
              </li>
            );
          })}
        </ol>
      </div>
      <div className="flex flex-col gap-2">
        <h3 className="font-bold">{t('pregnancy.dangerSigns')}</h3>
        <ul className="flex flex-col gap-1">
          {open.dangerSigns.map(s => <li key={s} className="inline-flex items-center gap-2"><TriangleAlert aria-hidden className="size-4 shrink-0" />{t(`pregnancy.dangerSign.${s}`)}</li>)}
        </ul>
        <p className="text-sm text-muted-foreground">{open.dangerSignsCounselledAt
          ? t('pregnancy.counselledBy', { author: open.dangerSignsCounselledBy ?? t('pregnancy.someone'), date: formatDate(new Date(open.dangerSignsCounselledAt), p.timeZone) })
          : t('pregnancy.notCounselled')}</p>
      </div>
      {!open.rulesSignedOff && <p className="text-sm text-muted-foreground">{t('pregnancy.awaitingReview')}</p>}
      {p.form === 'readings' && <ReadingsFields p={p} />}
      {p.form === 'close' && <CloseFields p={p} />}
      {p.form === null && (
        <div className="flex flex-wrap gap-2">
          {p.canRecord && <Button type="button" variant="outline" disabled={p.saving} onClick={p.onCounselled}>{t('pregnancy.counsel')}</Button>}
          {p.canRecord && <Button type="button" variant="outline" onClick={() => p.onOpen('readings')}>{t('pregnancy.readings')}</Button>}
          {p.canManage && <Button type="button" variant="outline" onClick={() => p.onOpen('close')}>{t('pregnancy.close')}</Button>}
        </div>
      )}
    </>
  );
}

function Stat({ label, value, note }: { label: string; value: ReactNode; note: string }) {
  return (
    <div className="flex flex-col rounded-md border p-3">
      <dt className="text-xs font-bold text-muted-foreground">{label}</dt>
      <dd className="text-lg font-extrabold">{value}</dd>
      {note && <dd className="text-sm text-muted-foreground">{note}</dd>}
    </div>
  );
}

type FieldName = keyof StartForm | keyof ReadingsForm | keyof CloseForm;

/** What is wrong with one field, in words that name what to enter: a missing value says which, anything else asks to check it. */
function useFieldError(errors: FieldErrors) {
  const { t } = useI18n();
  return (name: FieldName) => {
    const e = errors[name];
    if (!e) return undefined;
    return e.message === 'required' && (name === 'lmp' || name === 'scanWeeks' || name === 'fundalHeightCm') ? t(`pregnancy.error.required.${name}`)
      : name === 'outcome' ? t('pregnancy.error.required.outcome') : t('pregnancy.fieldInvalid');
  };
}

/** After a failed save: the fields in error, each linked to its control; it takes focus once per submit. */
function Summary({ form, names, className }: {
  form: UseFormReturn<StartForm> | UseFormReturn<ReadingsForm> | UseFormReturn<CloseForm>; names: readonly FieldName[]; className?: string;
}) {
  const { t } = useI18n();
  const { errors, submitCount } = form.formState;
  const error = useFieldError(errors);
  const list = names.flatMap(name => {
    const message = error(name);
    return message ? [{ id: `pregnancy-${name}`, label: t(`pregnancy.field.${name}`), message }] : [];
  });
  return <ErrorSummary title={t('field.errorSummary')} focusKey={submitCount} errors={list} className={className} />;
}

function StartFields({ p }: { p: Props }) {
  const { t } = useI18n();
  const { register, formState: { errors } } = p.startForm;
  const error = useFieldError(errors);
  const num = (name: 'scanWeeks' | 'scanDays' | 'gravida' | 'para' | 'abortions' | 'living') => (
    <Field id={`pregnancy-${name}`} label={t(`pregnancy.field.${name}`)} error={error(name)}>
      <Input type="number" inputMode="numeric" className="w-28" {...register(name)} />
    </Field>
  );
  return (
    <form noValidate onSubmit={p.onStart} className="flex flex-col gap-3">
      <Summary form={p.startForm} names={['lmp', 'scanDate', 'scanWeeks', 'scanDays', 'gravida', 'para', 'abortions', 'living']} />
      <div className="flex flex-wrap gap-3">
        {(['lmp', 'scanDate'] as const).map(name => (
          <Field key={name} id={`pregnancy-${name}`} label={t(`pregnancy.field.${name}`)} error={error(name)}>
            <Input type="date" className="w-44" {...register(name)} />
          </Field>
        ))}
        {num('scanWeeks')}{num('scanDays')}
      </div>
      <div className="flex flex-wrap gap-3">{num('gravida')}{num('para')}{num('abortions')}{num('living')}</div>
      <p className="text-sm text-muted-foreground">{t('pregnancy.startHint')}</p>
      <Actions p={p} />
    </form>
  );
}

function ReadingsFields({ p }: { p: Props }) {
  const { t } = useI18n();
  const { register, formState: { errors } } = p.readingsForm;
  const error = useFieldError(errors);
  return (
    <form noValidate onSubmit={p.onReadings} className="flex flex-col gap-3">
      <Summary form={p.readingsForm} names={['fundalHeightCm', 'fetalHeartRate']} />
      <div className="flex flex-wrap gap-3">
        {(['fundalHeightCm', 'fetalHeartRate'] as const).map(name => (
          <Field key={name} id={`pregnancy-${name}`} label={t(`pregnancy.field.${name}`)} error={error(name)}>
            <Input type="number" inputMode="decimal" step="0.1" className="w-32" {...register(name)} />
          </Field>
        ))}
      </div>
      <p className="text-sm text-muted-foreground">{t('pregnancy.readingsHint')}</p>
      <Actions p={p} />
    </form>
  );
}

function CloseFields({ p }: { p: Props }) {
  const { t } = useI18n();
  const error = useFieldError(p.closeForm.formState.errors);
  return (
    <form noValidate onSubmit={p.onClose} className="flex flex-wrap items-end gap-3">
      <Summary form={p.closeForm} names={['outcome']} className="basis-full" />
      <Select value={p.closeForm.watch('outcome') || undefined} onValueChange={v => p.closeForm.setValue('outcome', v as CloseForm['outcome'], { shouldValidate: true })}>
        <Field id="pregnancy-outcome" label={t('pregnancy.field.outcome')} required error={error('outcome')}>
          <SelectTrigger className="w-56"><SelectValue placeholder={t('pregnancy.outcomePlaceholder')} /></SelectTrigger>
        </Field>
        <SelectContent>{outcomes.map(o => <SelectItem key={o} value={o}>{t(`pregnancy.outcome.${o}`)}</SelectItem>)}</SelectContent>
      </Select>
      <Actions p={p} />
    </form>
  );
}

function Actions({ p }: { p: Props }) {
  const { t } = useI18n();
  return (
    <div className="flex flex-wrap gap-2">
      <Button type="submit" disabled={p.saving}>{t('pregnancy.save')}</Button>
      <Button type="button" variant="outline" onClick={p.onCancel}>{t('pregnancy.cancel')}</Button>
    </div>
  );
}
