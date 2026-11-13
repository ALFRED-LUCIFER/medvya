import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { CircleCheck, TriangleAlert } from 'lucide-react';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, Input, Label, Textarea } from '@medvya/ui';
import { isVitalsField, problemKey, undecided, vitalKinds, vitalUnits, weightChange, type IntakeForm, type IntakeProblem, type VitalKind, type VitalView } from '../domain/intake';

export type VitalsFormProps = {
  form: UseFormReturn<IntakeForm>; vitals: VitalView[]; timeZone: string; readOnly: boolean; online: boolean;
  problem: IntakeProblem | null; badKind: VitalKind | null; saving: boolean; keeping: VitalKind | null;
  onSave: FormEventHandler<HTMLFormElement>; onKeep: (kind: VitalKind) => void; onRemeasure: (kind: VitalKind) => void;
  /** The nurse asks the chief complaint here; in the consultation it is in the note. */
  withComplaint: boolean;
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
      <div className="grid gap-4 sm:grid-cols-2">
        {vitalKinds.map(kind => (
          <VitalField key={kind} kind={kind} form={p.form} timeZone={p.timeZone} view={p.vitals.find(v => v.kind === kind)} readOnly={p.readOnly}
            invalid={errors[kind] !== undefined ? 'value' : p.badKind === kind && p.problem?.kind === 'invalid' ? p.problem.code : null}
            keeping={p.keeping === kind} onKeep={p.onKeep} onRemeasure={p.onRemeasure} />
        ))}
      </div>
      {p.problem?.kind === 'invalid' && p.badKind === null && isVitalsField(p.problem.field) && <p role="alert" className={fieldError}>{t(problemKey(p.problem))}</p>}
      {!p.readOnly && <Button type="submit" className="self-start" loading={p.saving} disabled={!p.online}>{t('intake.save')}</Button>}
    </form>
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
                <Button type="button" variant="outline" className="whitespace-normal" aria-label={t('intake.action.remeasureFor', { name })} onClick={() => onRemeasure(kind)}>{t('intake.action.remeasure')}</Button>
                <Button type="button" variant="outline" className="whitespace-normal" loading={keeping} aria-label={t('intake.action.keepFor', { name })} onClick={() => onKeep(kind)}>{t('intake.action.keep')}</Button>
              </div>
            )}
        </div>
      )}
    </div>
  );
}
