import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { Link } from 'react-router';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Input, Textarea } from '@medvya/ui';
import { CircleAlert, CircleCheck, Lightbulb } from 'lucide-react';
import { Field } from '../../settings/components/Field';
import { describedBy, fieldError } from '../../settings/components/fieldStyles';
import { Section } from '../../settings/components/Section';
import { geriatricFields, geriLines, type GeriError, type GeriatricAssessment, type GeriatricForm, type MedicineReview } from '../domain/olderAdult';

type Props = {
  latest: GeriatricAssessment | null; timeZone: string; rulesSignedOff: boolean; loading: boolean; failed: boolean;
  review: MedicineReview | null; reviewHidden: boolean; reviewFailed: boolean;
  form: UseFormReturn<GeriatricForm>; recording: boolean; saving: boolean; saveFailed: boolean; saveForbidden: boolean;
  onRecord: () => void; onCancel: () => void; onSubmit: FormEventHandler<HTMLFormElement>;
  planHint: string | null; onPlanChange: (medicine: string) => void;
  /** The caller's open consultation with this patient: "plan change" opens it. */
  consultPath: string | null;
};

/**
 * The older adult cards on Patient 360 (MDV-P2-037, E-01, design `geri` and `deprescribe`): the latest geriatric assessment with its flags in
 * words and an icon (never colour alone), a new assessment, and the medicine review as suggestions the doctor decides on.
 */
export default function OlderAdultCards(p: Props) {
  const { t, formatDate } = useI18n();
  return (
    <>
      <Section title={t('olderAdult.title')}>
        {p.loading ? <p role="status">{t('patient360.loading')}</p>
          : p.failed ? <p role="alert" className={fieldError}>{t('patient360.loadFailed')}</p>
          : p.latest ? (
            <>
              <p className="text-sm text-muted-foreground">
                {t(p.latest.author ? 'olderAdult.updatedBy' : 'olderAdult.updated', { date: formatDate(new Date(p.latest.assessedAt), p.timeZone), author: p.latest.author ?? '' })}
              </p>
              <ul className="grid gap-x-6 gap-y-3 sm:grid-cols-2">
                {geriLines(p.latest).map(l => (
                  <li key={l.key} className="flex items-start gap-2">
                    {l.level === 'alert' ? <CircleAlert aria-hidden className="mt-0.5 size-4 shrink-0 text-danger-700" />
                      : l.level === 'ok' ? <CircleCheck aria-hidden className="mt-0.5 size-4 shrink-0" /> : <span aria-hidden className="size-4 shrink-0" />}
                    <span className="flex flex-col">
                      <strong>{t(`olderAdult.line.${l.key}`)}</strong>
                      <span className={l.level === 'alert' ? 'font-bold' : 'text-muted-foreground'}>{t(`olderAdult.value.${l.key}`, l.values)}</span>
                    </span>
                  </li>
                ))}
              </ul>
            </>
          ) : <p className="text-muted-foreground">{t('olderAdult.empty')}</p>}
        {!p.rulesSignedOff && !p.loading && <p className="text-sm text-muted-foreground">{t('olderAdult.rulesDraft')}</p>}
        {p.recording ? <AssessmentForm {...p} /> : <div><Button variant="outline" className="min-h-11" onClick={p.onRecord}>{t('olderAdult.record')}</Button></div>}
      </Section>
      {p.review && !p.reviewHidden && p.review.olderAdult && <ReviewCard review={p.review} consultPath={p.consultPath} planHint={p.planHint} onPlanChange={p.onPlanChange} />}
      {p.reviewFailed && <p role="alert" className={fieldError}>{t('olderAdult.review.failed')}</p>}
    </>
  );
}

function AssessmentForm(p: Props) {
  const { t } = useI18n();
  const { errors } = p.form.formState;
  const message = (code?: string) => code && t(`olderAdult.error.${code as GeriError}`);
  return (
    <form noValidate onSubmit={p.onSubmit} aria-label={t('olderAdult.form')} className="flex flex-col gap-4 border-t pt-4">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {geriatricFields.map(f => (
          <Field key={f} id={`geri-${f}`} label={t(`olderAdult.field.${f}`)} error={message(errors[f]?.message)}>
            <Input id={`geri-${f}`} inputMode={f === 'timedUpAndGoSeconds' ? 'decimal' : 'numeric'} dir="ltr" autoComplete="off" className="h-11"
              {...describedBy(`geri-${f}`, !!errors[f])} {...p.form.register(f)} />
          </Field>
        ))}
      </div>
      <Field id="geri-nutritionNote" label={t('olderAdult.field.nutritionNote')} error={message(errors.nutritionNote?.message)}>
        <Textarea id="geri-nutritionNote" rows={2} {...describedBy('geri-nutritionNote', !!errors.nutritionNote)} {...p.form.register('nutritionNote')} />
      </Field>
      <p className="text-sm text-muted-foreground">{t('olderAdult.newRow')}</p>
      <div aria-live="polite">
        {p.saveFailed && <p role="alert" className={fieldError}>{t('olderAdult.saveFailed')}</p>}
        {p.saveForbidden && <p role="alert" className={fieldError}>{t('olderAdult.saveForbidden')}</p>}
      </div>
      <div className="flex flex-wrap gap-3">
        <Button type="submit" className="min-h-11" loading={p.saving}>{t('olderAdult.save')}</Button>
        <Button type="button" variant="outline" className="min-h-11" onClick={p.onCancel}>{t('olderAdult.cancel')}</Button>
      </div>
    </form>
  );
}

function ReviewCard({ review, consultPath, planHint, onPlanChange }: { review: MedicineReview; consultPath: string | null; planHint: string | null; onPlanChange: (m: string) => void }) {
  const { t } = useI18n();
  return (
    <Section title={t('olderAdult.review.title', { count: review.medicines })}>
      <p className="inline-flex items-center gap-1.5 self-start rounded-md border px-2 py-0.5 text-sm font-bold"><Lightbulb aria-hidden className="size-4 shrink-0" />{t('olderAdult.review.decides')}</p>
      {review.suggestions.length === 0 ? <p className="text-muted-foreground">{t('olderAdult.review.none')}</p> : (
        <ul className="flex flex-col divide-y rounded-lg border">
          {review.suggestions.map(s => (
            <li key={`${s.medicineId}:${s.rule}`} className="flex flex-wrap items-start justify-between gap-3 p-3">
              <span className="flex min-w-0 flex-1 basis-64 items-start gap-2">
                <CircleAlert aria-hidden className="mt-0.5 size-4 shrink-0" />
                <span className="flex flex-col">
                  <strong>{s.medicine}</strong>
                  <span className="text-muted-foreground">{t(`olderAdult.rule.${s.rule}`, {
                    weeks: s.weeksOnChart ?? 0, egfr: ltr(String(s.egfr ?? '')), potassium: ltr(((s.potassiumHundredths ?? 0) / 100).toFixed(1)),
                  })}</span>
                </span>
              </span>
              {consultPath
                ? <Link to={consultPath} aria-label={t('olderAdult.review.planFor', { medicine: s.medicine })} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('olderAdult.review.plan')}</Link>
                : <Button variant="outline" className="min-h-11" aria-label={t('olderAdult.review.planFor', { medicine: s.medicine })} onClick={() => onPlanChange(s.medicine)}>{t('olderAdult.review.plan')}</Button>}
            </li>
          ))}
        </ul>
      )}
      <div aria-live="polite">{planHint && <p role="status" className="font-bold">{t('olderAdult.review.noConsult', { medicine: planHint })}</p>}</div>
      {review.notChecked.length > 0 && (
        <ul className="flex flex-col gap-1 text-sm text-muted-foreground">
          {review.notChecked.map(n => <li key={`${n.medicineId}:${n.reason}`}>{t(`olderAdult.notChecked.${n.reason}`, { medicine: n.medicine })}</li>)}
        </ul>
      )}
      {!review.rulesSignedOff && <p className="text-sm text-muted-foreground">{t('olderAdult.review.draft')}</p>}
    </Section>
  );
}
