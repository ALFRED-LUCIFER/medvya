import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import { Button } from '@medvya/ui';
import { fieldError } from '../../settings/components/fieldStyles';
import { Section } from '../../settings/components/Section';
import { clinicalPolicies, type ClinicalPolicyForm } from '../domain/clinicalAccess';

type Props = {
  form: UseFormReturn<ClinicalPolicyForm>; loading: boolean; forbidden: boolean; loadFailed: boolean;
  saving: boolean; saved: boolean; saveFailed: boolean; onSubmit: FormEventHandler<HTMLFormElement>;
};

/** Who opens patient records (MDV-P2-024): every clinician of the facility, or only the patient's own doctor and the day's nurse. */
export default function ClinicalPolicyPanel(p: Props) {
  const { t } = useI18n();
  if (p.loading) return <p role="status">{t('settings.loading')}</p>;
  if (p.loadFailed) return <p role="alert" className={fieldError}>{t(p.forbidden ? 'settings.forbidden' : 'settings.loadFailed')}</p>;
  return (
    <Section title={t('clinicalAccess.policy.title')} intro={t('clinicalAccess.policy.intro')}>
      <form noValidate onSubmit={p.onSubmit} className="flex flex-col gap-4">
        <fieldset className="flex flex-col gap-2">
          <legend className="sr-only">{t('clinicalAccess.policy.title')}</legend>
          {clinicalPolicies.map(policy => (
            <label key={policy} className="flex min-h-11 items-start gap-3 rounded-lg border p-3">
              <input type="radio" value={policy} {...p.form.register('policy')} className="mt-1 size-4 accent-brand-700" aria-describedby={`policy-${policy}`} />
              <span className="flex flex-col">
                <strong>{t(`clinicalAccess.policy.${policy}`)}</strong>
                <span id={`policy-${policy}`} className="text-sm text-muted-foreground">{t(`clinicalAccess.policy.${policy}.hint`)}</span>
              </span>
            </label>
          ))}
        </fieldset>
        <p className="text-sm text-muted-foreground">{t('clinicalAccess.policy.requestNote')}</p>
        <div className="flex flex-wrap items-center gap-3">
          <Button type="submit" loading={p.saving}>{t('settings.save')}</Button>
          {p.saved && <p role="status">{t('settings.saved')}</p>}
          {p.saveFailed && <p role="alert" className={fieldError}>{t('settings.saveFailed')}</p>}
        </div>
      </form>
    </Section>
  );
}
