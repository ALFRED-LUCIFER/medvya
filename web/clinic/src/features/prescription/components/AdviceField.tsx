import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { CircleCheck, Languages } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Label, Textarea } from '@medvya/ui';
import type { AdviceForm, RxAdvice } from '../domain/authorisation';

type Props = {
  advice: RxAdvice | null; loading: boolean; failed: boolean; ended: boolean;
  form: UseFormReturn<AdviceForm>; saving: boolean; saved: boolean; saveFailed: boolean; onSave: FormEventHandler<HTMLFormElement>;
};

/**
 * Advice for the patient (MDV-P2-043, design `advice`): written once in the plan; it prints on the prescription and goes with the shared copy.
 * When the patient's language has no reviewed translation it is English, and the screen says so (never machine-translated).
 */
export default function AdviceField(p: Props) {
  const { t, locale } = useI18n();
  if (p.loading) return <p role="status" className="text-sm">{t('plan.advice.loading')}</p>;
  if (p.failed || !p.advice) return <p role="alert" className="text-sm font-bold text-danger-700">{t('plan.advice.loadFailed')}</p>;
  const language = p.advice.patientLanguage && (new Intl.DisplayNames([locale], { type: 'language' }).of(p.advice.patientLanguage) ?? p.advice.patientLanguage);
  return (
    <form noValidate onSubmit={p.onSave} className="flex flex-col gap-2">
      <Label htmlFor="plan-advice" className="font-bold">{t('plan.advice.label')}</Label>
      <Textarea id="plan-advice" className="min-h-24" maxLength={1000} readOnly={p.ended} aria-describedby="plan-advice-language" {...p.form.register('text')} />
      <p id="plan-advice-language" className="inline-flex items-start gap-1.5 text-sm text-muted-foreground">
        <Languages aria-hidden className="mt-0.5 size-4 shrink-0" />
        {p.advice.inPatientLanguage || !language ? t('plan.advice.english') : t('plan.advice.noTranslation', { language })}
      </p>
      {!p.ended && (
        <div className="flex flex-wrap items-center gap-3">
          <Button type="submit" variant="outline" loading={p.saving}>{t('plan.advice.save')}</Button>
          {p.saved && <p role="status" className="inline-flex items-center gap-1.5 text-sm font-bold"><CircleCheck aria-hidden className="size-4" />{t('plan.advice.saved')}</p>}
        </div>
      )}
      {p.saveFailed && <p role="alert" className="text-sm font-bold text-danger-700">{t('plan.advice.saveFailed')}</p>}
    </form>
  );
}
