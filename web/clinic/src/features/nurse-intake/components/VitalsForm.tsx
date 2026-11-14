import type { FormEventHandler, KeyboardEvent } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { CircleCheck, PencilLine, TriangleAlert } from 'lucide-react';
import { ltr, useI18n } from '@medvya/i18n';
import { Banner, Button, Input, Label, Textarea } from '@medvya/ui';
import { confirmAll, isVitalsField, problemKey, undecided, vitalKinds, vitalUnits, weightChange, type CorrectionForm, type IntakeForm, type IntakeProblem, type VitalKind, type VitalView } from '../domain/intake';

/** The nurse's correction after the handoff, before the doctor starts (MDV-P2-048); absent where it does not apply. */
export type CorrectionProps = {
  form: UseFormReturn<CorrectionForm>; correcting: VitalKind | null; saving: boolean;
  onStart: (kind: VitalKind) => void; onCancel: () => void; onSave: () => void;
};

export type VitalsFormProps = {
  form: UseFormReturn<IntakeForm>; vitals: VitalView[]; timeZone: string; readOnly: boolean; online: boolean;
  problem: IntakeProblem | null; badKind: VitalKind | null; saving: boolean; keeping: VitalKind | null;
  onSave: FormEventHandler<HTMLFormElement>; onKeep: (kind: VitalKind) => void; onRemeasure: (kind: VitalKind) => void;
  /** The nurse asks the chief complaint here; in the consultation it is in the note. */
  withComplaint: boolean;
  /** One confirmation for every flagged reading (MDV-P2-048). */
  keepingAll?: boolean; onKeepAll?: () => void;
  correction?: CorrectionProps;
};

const fieldError = 'text-sm font-semibold text-danger-700';

/** The vitals (weight and height included) with the previous reading and the decision on a flagged one: at nurse intake and in the consultation. */
export default function VitalsForm(p: VitalsFormProps) {
  const { t } = useI18n();
  const { errors } = p.form.formState;
  return (
    <form noValidate onSubmit={p.onSave} className="flex flex-col gap-4 rounded-xl border bg-card p-4">
      {p.withComplaint && (
        <>
          <div className="flex flex-col gap-2">
            <Label htmlFor="intake-complaint" className="text-base font-bold">{t('intake.complaint')}</Label>
            <Textarea id="intake-complaint" rows={2} maxLength={500} readOnly={p.readOnly} className="min-h-14 text-base" {...p.form.register('chiefComplaint')} />
          </div>
          <h2 className="text-lg font-extrabold">{t('intake.vitals')}</h2>
        </>
      )}
      {!p.readOnly && p.onKeepAll && confirmAll(p.vitals) && <FlaggedList vitals={p.vitals} keeping={p.keepingAll ?? false} online={p.online} onKeepAll={p.onKeepAll} />}
      <div className="grid gap-4 sm:grid-cols-2">
        {vitalKinds.map(kind => (
          <VitalField key={kind} kind={kind} form={p.form} timeZone={p.timeZone} view={p.vitals.find(v => v.kind === kind)} readOnly={p.readOnly}
            invalid={errors[kind] !== undefined ? 'value' : p.badKind === kind && p.problem?.kind === 'invalid' ? p.problem.code : null}
            keeping={p.keeping === kind} onKeep={p.onKeep} onRemeasure={p.onRemeasure} correction={p.correction} online={p.online} />
        ))}
      </div>
      {p.problem?.kind === 'invalid' && p.badKind === null && isVitalsField(p.problem.field) && <p role="alert" className={fieldError}>{t(problemKey(p.problem))}</p>}
      {!p.readOnly && <Button type="submit" className="self-start" loading={p.saving} disabled={!p.online}>{t('intake.save')}</Button>}
    </form>
  );
}

/**
 * Several flagged readings in one list, each in words with its usual range, and one confirmation for all (MDV-P2-048). Each reading can still be
 * re-measured on its own below. Wording awaits the pilot doctor and nurse (validation plan question 16).
 */
function FlaggedList({ vitals, keeping, online, onKeepAll }: { vitals: VitalView[]; keeping: boolean; online: boolean; onKeepAll: () => void }) {
  const { t } = useI18n();
  return (
    <Banner tone="warning" icon={TriangleAlert} title={t('intake.flagged.title')}
      action={<Button type="button" variant="outline" className="whitespace-normal" loading={keeping} disabled={!online} onClick={onKeepAll}>{t('intake.flagged.confirm')}</Button>}>
      <ul className="flex flex-col gap-0.5">
        {vitals.filter(undecided).map(v => (
          <li key={v.kind}>
            <span className="font-bold">{t(`intake.vital.${v.kind}`)}</span>{' '}<bdi dir="ltr" className="tabular-nums">{`${v.value} ${vitalUnits[v.kind]}`}</bdi>
            {v.flag !== 'Changed' && <>{' · '}{t(`intake.flagged.${v.flag!}`)}{rangeText(v) && <>{' · '}<RangeText view={v} /></>}</>}
            {v.flag === 'Changed' && <>{' · '}{t('intake.flagged.Changed')}</>}
          </li>
        ))}
      </ul>
    </Banner>
  );
}

/** The usual range in words for a High or Low reading: "Usual: below 140 mmHg" or "Usual: 94 % or above"; nothing without a range. */
const rangeText = (v: VitalView) => (v.flag === 'High' && v.usualHigh != null) || (v.flag === 'Low' && v.usualLow != null);
function RangeText({ view }: { view: VitalView }) {
  const { t } = useI18n();
  const unit = ltr(vitalUnits[view.kind]);
  return <>{view.flag === 'High' ? t('intake.range.below', { value: ltr(String(view.usualHigh)), unit }) : t('intake.range.atLeast', { value: ltr(String(view.usualLow)), unit })}</>;
}

type FieldProps = {
  kind: VitalKind; form: UseFormReturn<IntakeForm>; view: VitalView | undefined; timeZone: string; readOnly: boolean; invalid: string | null;
  keeping: boolean; onKeep: (kind: VitalKind) => void; onRemeasure: (kind: VitalKind) => void; correction: CorrectionProps | undefined; online: boolean;
};

/** One vital sign: a large input, the previous reading, and when the saved reading is flagged, the flag in words with its two ways forward. */
function VitalField({ kind, form, view, timeZone, readOnly, invalid, keeping, onKeep, onRemeasure, correction, online }: FieldProps) {
  const { t, formatDate, formatTime } = useI18n();
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
          {rangeText(view) && <p className="text-sm"><RangeText view={view} /></p>}
          {view.flagKept
            ? <p className="inline-flex items-center gap-1.5 text-sm font-semibold"><CircleCheck aria-hidden className="size-4" />{t('intake.flag.kept')}</p>
            : undecided(view) && !readOnly && (
              <div className="flex flex-wrap gap-2">
                <Button type="button" variant="outline" className="whitespace-normal" aria-label={t('intake.action.remeasureFor', { name })} onClick={() => onRemeasure(kind)}>{t('intake.action.remeasure')}</Button>
                <Button type="button" variant="outline" className="whitespace-normal" loading={keeping} aria-label={t('intake.action.keepFor', { name })} onClick={() => onKeep(kind)}>{t('intake.action.keep')}</Button>
              </div>
            )}
        </div>
      )}
      {view?.correction && (
        <div className="flex flex-col gap-0.5 rounded-md border border-info-border bg-info-bg p-2 text-sm text-info-text">
          <p className="inline-flex items-center gap-1.5 font-bold"><PencilLine aria-hidden className="size-4" />
            {t('intake.corrected', { value: ltr(`${view.correction.fromValue} ${vitalUnits[kind]}`), time: formatTime(new Date(view.correction.fromAt), timeZone) })}</p>
          <p>{t('intake.corrected.reason', { reason: view.correction.reason })}</p>
        </div>
      )}
      {view && correction && (correction.correcting === kind
        ? <CorrectField kind={kind} name={name} correction={correction} online={online} />
        : <Button type="button" variant="outline" className="self-start whitespace-normal" aria-label={t('intake.correct.for', { name })} onClick={() => correction.onStart(kind)}>
            <PencilLine aria-hidden />{t('intake.correct')}
          </Button>)}
    </div>
  );
}

/**
 * The correction of one reading (MDV-P2-048): the right value and why. Inside the vitals form, so it is a group with its own buttons, and Enter in it
 * saves the correction, never the vitals form.
 */
function CorrectField({ kind, name, correction, online }: { kind: VitalKind; name: string; correction: CorrectionProps; online: boolean }) {
  const { t } = useI18n();
  const { errors } = correction.form.formState;
  const id = `intake-${kind}-correct`;
  const onKey = (e: KeyboardEvent<HTMLDivElement>) => { if (e.key === 'Enter' && e.target instanceof HTMLInputElement) { e.preventDefault(); correction.onSave(); } };
  return (
    <div role="group" aria-label={t('intake.correct.for', { name })} onKeyDown={onKey} className="flex flex-col gap-2 rounded-md border p-2">
      <Label htmlFor={id} className="font-bold">{t('intake.correct.value')}</Label>
      <div className="flex items-center gap-2">
        <Input id={id} inputMode="decimal" autoComplete="off" autoFocus className="max-w-[9rem] text-body-large" aria-invalid={!!errors.value}
          aria-describedby={errors.value ? `${id}-error` : undefined} {...correction.form.register('value')} />
        <span className="text-muted-foreground">{ltr(vitalUnits[kind])}</span>
      </div>
      {errors.value && <p id={`${id}-error`} className={fieldError}>{t('intake.error.value')}</p>}
      <Label htmlFor={`${id}-reason`} className="font-bold">{t('intake.correct.reason')}</Label>
      <Textarea id={`${id}-reason`} rows={2} maxLength={500} className="min-h-14 text-body-large" aria-invalid={!!errors.reason}
        aria-describedby={errors.reason ? `${id}-reason-error` : undefined} {...correction.form.register('reason')} />
      {errors.reason && <p id={`${id}-reason-error`} className={fieldError}>{t('intake.correct.reasonRequired')}</p>}
      <div className="flex flex-wrap gap-2">
        <Button type="button" loading={correction.saving} disabled={!online} onClick={correction.onSave}>{t('intake.correct.save')}</Button>
        <Button type="button" variant="outline" onClick={correction.onCancel}>{t('intake.correct.cancel')}</Button>
      </div>
    </div>
  );
}
