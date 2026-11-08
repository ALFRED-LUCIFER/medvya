import type { FormEventHandler } from 'react';
import { Link } from 'react-router';
import type { UseFormReturn } from 'react-hook-form';
import { CircleCheck, TriangleAlert } from 'lucide-react';
import { ltr, useI18n, type MessageKey } from '@medvya/i18n';
import { Button, buttonVariants, Input, Label, Textarea } from '@medvya/ui';
import {
  undecided, vitalKinds, vitalUnits, weightChange, type IntakeForm, type IntakeProblem, type IntakeScreen, type VitalKind, type VitalView,
} from '../domain/intake';

type Props = {
  screen: IntakeScreen | null; loading: boolean; notFound: boolean; refused: boolean; failed: boolean;
  form: UseFormReturn<IntakeForm>;
  escalationForm: UseFormReturn<{ reason: string }>;
  problem: IntakeProblem | null; badKind: VitalKind | null;
  saving: boolean; keeping: VitalKind | null; escalating: boolean; sendingEscalation: boolean; handingOff: boolean; dirty: boolean; online: boolean;
  onSave: FormEventHandler<HTMLFormElement>; onKeep: (kind: VitalKind) => void; onRemeasure: (kind: VitalKind) => void;
  onToggleEscalate: () => void; onEscalate: FormEventHandler<HTMLFormElement>; onHandOff: () => void;
  queuePath: string;
};

const fieldError = 'text-sm font-semibold text-danger-700';

/**
 * N-01 Nurse intake (MDV-P2-007): identity check, the chief complaint in the patient's words, large vitals inputs with the previous value, out-of-range
 * flags in words with an icon (never colour alone) that need a re-measure or an explicit "keep", escalation to the doctor with a reason, and the handoff,
 * which stays disabled with its reason while anything blocks it. Built for a tablet: every target is at least 44 px.
 */
export default function IntakePage(p: Props) {
  const { t } = useI18n();
  if (p.loading) return <p role="status">{t('intake.loading')}</p>;
  if (p.refused || p.notFound || p.failed || !p.screen)
    return <Message title={t(p.refused ? 'intake.refused' : p.notFound ? 'intake.notFound' : 'intake.loadFailed')} queuePath={p.queuePath} />;
  const { intake, patient, timeZone } = p.screen;
  const withNurse = intake.state === 'WithNurse';
  const { errors } = p.form.formState;
  const readOnly = !withNurse;
  const blocked = intake.blockers.length > 0 || p.dirty;
  const reason: MessageKey = intake.blockers.includes('vitals_undecided') ? 'intake.handOff.blockedVitals' : 'intake.handOff.unsaved';

  return (
    <div className="flex max-w-[960px] flex-col gap-4">
      <Identity patient={patient} />
      {readOnly && <p role="status" className="font-bold">{t('intake.handedOff')}</p>}
      <form noValidate onSubmit={p.onSave} className="flex flex-col gap-4 rounded-xl border bg-card p-4">
        <div className="flex flex-col gap-2">
          <Label htmlFor="intake-complaint" className="text-base font-bold">{t('intake.complaint')}</Label>
          <Textarea id="intake-complaint" rows={2} maxLength={500} readOnly={readOnly} className="min-h-14 text-base" {...p.form.register('chiefComplaint')} />
        </div>
        <h2 className="text-lg font-extrabold">{t('intake.vitals')}</h2>
        <div className="grid gap-4 sm:grid-cols-2">
          {vitalKinds.map(kind => (
            <VitalField key={kind} kind={kind} form={p.form} timeZone={timeZone} view={intake.vitals.find(v => v.kind === kind)} readOnly={readOnly}
              invalid={errors[kind] !== undefined ? 'value' : p.badKind === kind && p.problem?.kind === 'invalid' ? p.problem.code : null}
              keeping={p.keeping === kind} onKeep={p.onKeep} onRemeasure={p.onRemeasure} />
          ))}
        </div>
        {p.problem?.kind === 'invalid' && p.badKind === null && <p role="alert" className={fieldError}>{t(problemKey(p.problem))}</p>}
        {!readOnly && <Button type="submit" className="min-h-11 self-start" loading={p.saving} disabled={!p.online}>{t('intake.save')}</Button>}
      </form>

      {p.problem && p.problem.kind !== 'invalid' && <p role="alert" className={fieldError}>{t(problemKey(p.problem))}</p>}
      {intake.escalated && <p role="status" className="inline-flex items-center gap-1.5 font-bold"><TriangleAlert aria-hidden className="size-4" />{t('intake.escalated')}</p>}

      {withNurse && (
        <div className="flex flex-col gap-3 rounded-xl border bg-card p-4">
          <div className="flex flex-wrap items-center gap-3">
            <Button variant="outline" className="min-h-11" aria-expanded={p.escalating} onClick={p.onToggleEscalate}>{t('intake.escalate')}</Button>
            <Button variant="navy" className="min-h-11 whitespace-normal" loading={p.handingOff} disabled={blocked || !p.online} aria-describedby={blocked ? 'intake-handoff-why' : undefined} onClick={p.onHandOff}>
              {t('intake.handOff')}
            </Button>
            <Link to={p.queuePath} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('intake.back')}</Link>
          </div>
          {blocked && <p id="intake-handoff-why" className="text-sm font-semibold">{t(reason)}</p>}
          {p.escalating && (
            <form noValidate onSubmit={p.onEscalate} className="flex flex-col gap-2">
              <Label htmlFor="intake-reason" className="font-bold">{t('intake.escalate.reason')}</Label>
              <Textarea id="intake-reason" rows={2} maxLength={500} className="min-h-14 text-base" aria-invalid={!!p.escalationForm.formState.errors.reason}
                aria-describedby={p.escalationForm.formState.errors.reason ? 'intake-reason-error' : undefined} {...p.escalationForm.register('reason')} />
              {p.escalationForm.formState.errors.reason && <p id="intake-reason-error" className={fieldError}>{t('intake.escalate.required')}</p>}
              <Button type="submit" className="min-h-11 self-start" loading={p.sendingEscalation} disabled={!p.online}>{t('intake.escalate.send')}</Button>
            </form>
          )}
        </div>
      )}
      {readOnly && <Link to={p.queuePath} className={buttonVariants({ variant: 'outline', className: 'min-h-11 self-start' })}>{t('intake.back')}</Link>}
    </div>
  );
}

const knownCodes = ['implausible', 'too_precise', 'systolic_not_above_diastolic', 'not_with_nurse', 'handoff_blocked', 'not_flagged'] as const;

function problemKey(p: IntakeProblem): MessageKey {
  if (p.kind === 'invalid' || p.kind === 'conflict') return (knownCodes as readonly string[]).includes(p.code) ? `intake.error.${p.code as (typeof knownCodes)[number]}` : 'intake.error.failed';
  return p.kind === 'refused' ? 'intake.error.refused' : 'intake.error.failed';
}

function Identity({ patient }: { patient: IntakeScreen['patient'] }) {
  const { t, formatDate } = useI18n();
  return (
    <section aria-label={t('intake.identity.label')} className="flex flex-col gap-1 rounded-xl border-2 border-brand-700 bg-card p-4">
      <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1">
        <h1 className="text-xl font-extrabold">{patient.fullName}</h1>
        <span>{t('consult.banner.ageSex', { age: patient.ageYears, sex: t(`patients.sex.${patient.sexAtBirth}`) })}</span>
        <span>{t('consult.banner.mrn', { mrn: ltr(patient.mrn) })}</span>
        <span>{t('consult.banner.dob', { date: formatDate(new Date(`${patient.dateOfBirth}T12:00:00Z`), 'UTC') })}</span>
      </div>
      <p className="text-sm text-muted-foreground">{t('intake.identity.check')}</p>
    </section>
  );
}

type FieldProps = {
  kind: VitalKind; form: UseFormReturn<IntakeForm>; view: VitalView | undefined; timeZone: string; readOnly: boolean; invalid: string | null;
  keeping: boolean; onKeep: (kind: VitalKind) => void; onRemeasure: (kind: VitalKind) => void;
};

/** One vital sign: a large input, the previous reading, and when the saved reading is flagged, the flag in words with its two ways forward. */
function VitalField({ kind, form, view, timeZone, readOnly, invalid, keeping, onKeep, onRemeasure }: FieldProps) {
  const { t, formatDate } = useI18n();
  const name = t(`intake.vital.${kind}`);
  const id = `intake-${kind}`;
  const hint = `${id}-hint`;
  const previous = view?.previousValue != null && view.previousAt
    ? t('intake.previous', { value: ltr(String(view.previousValue)), unit: ltr(vitalUnits[kind]), date: formatDate(new Date(view.previousAt), timeZone) }) : t('intake.noPrevious');
  const change = view ? weightChange(view) : null;
  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={id} className="text-base font-bold">{name}</Label>
      <div className="flex items-center gap-2">
        <Input id={id} inputMode="decimal" autoComplete="off" readOnly={readOnly} className="h-14 max-w-[9rem] text-2xl font-bold" aria-invalid={invalid !== null}
          aria-describedby={hint} {...form.register(kind)} />
        <span className="text-base text-muted-foreground">{ltr(vitalUnits[kind])}</span>
      </div>
      <p id={hint} className="text-sm text-muted-foreground">{previous}</p>
      {invalid && <p role="alert" className={fieldError}>{t(invalid === 'value' ? 'intake.error.value' : invalid === 'too_precise' ? 'intake.error.too_precise' : 'intake.error.implausible')}</p>}
      {view && view.flag !== null && (
        <div className="flex flex-col gap-2 rounded-md border-2 border-warning-700 p-2">
          <p className="inline-flex items-center gap-1.5 font-bold">
            <TriangleAlert aria-hidden className="size-4" />
            {view.flag === 'Changed' ? t('intake.flag.Changed', { delta: ltr(change ?? ''), unit: ltr(vitalUnits[kind]), date: view.previousAt ? formatDate(new Date(view.previousAt), timeZone) : '' }) : t(`intake.flag.${view.flag}`)}
          </p>
          {view.flagKept
            ? <p className="inline-flex items-center gap-1.5 text-sm font-semibold"><CircleCheck aria-hidden className="size-4" />{t('intake.flag.kept')}</p>
            : undecided(view) && !readOnly && (
              <div className="flex flex-wrap gap-2">
                <Button type="button" variant="outline" className="min-h-11 whitespace-normal" aria-label={t('intake.action.remeasureFor', { name })} onClick={() => onRemeasure(kind)}>{t('intake.action.remeasure')}</Button>
                <Button type="button" variant="outline" className="min-h-11 whitespace-normal" loading={keeping} aria-label={t('intake.action.keepFor', { name })} onClick={() => onKeep(kind)}>{t('intake.action.keep')}</Button>
              </div>
            )}
        </div>
      )}
    </div>
  );
}

function Message({ title, queuePath }: { title: string; queuePath: string }) {
  const { t } = useI18n();
  return (
    <div role="alert" className="flex flex-col items-start gap-3 rounded-xl border bg-card p-6">
      <h1 className="text-lg font-extrabold">{title}</h1>
      <Link to={queuePath} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('intake.back')}</Link>
    </div>
  );
}
