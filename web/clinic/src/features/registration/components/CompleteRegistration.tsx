import type { UseFormReturn } from 'react-hook-form';
import { Link } from 'react-router';
import { ArrowLeft, CircleCheck, TriangleAlert } from 'lucide-react';
import { useI18n, type Country } from '@medvya/i18n';
import { Button, buttonVariants, cn, PageContainer, PageHeader } from '@medvya/ui';
import type { RegistrationStatus } from '../../patients/domain/directory';
import PatientsMessage from '../../patients/components/PatientsMessage';
import type { FormField, RegistrationForm } from '../domain/registration';
import { FormErrorSummary, TextField } from './FormFields';
import { AddressFields, EmergencyContactFields, PayerStep } from './RegistrationSteps';

type Props = {
  form: UseFormReturn<RegistrationForm>;
  country: Country;
  values: RegistrationForm;
  loading: boolean;
  saving: boolean;
  onSubmit: () => void;
  /** After a failed save: the fields in error, in screen order; focusKey changes once per failure. */
  errorSummary: { focusKey: number; fields: FormField[] };
  onReload: () => void;
  /** Set after a save: Complete closes the desk's completion task. */
  status: RegistrationStatus | null;
  failure: 'notFound' | 'forbidden' | 'changed' | 'error' | null;
  recordPath: string;
};

/** "Complete registration" for a quick-registered patient (MDV-P1-015): address and payer, from the record page. */
export default function CompleteRegistration(p: Props) {
  const { t } = useI18n();
  const back = (
    <Link to={p.recordPath} className={cn(buttonVariants({ variant: 'ghost' }), 'self-start px-2')}>
      <ArrowLeft className="rtl:rotate-180" aria-hidden />{t('registration.complete.back')}
    </Link>
  );
  // Announced: it answers the save (another facility's patient, or an old link); nothing was changed.
  if (p.failure === 'notFound')
    return <div className="flex flex-col gap-4">{back}<div role="alert"><PatientsMessage pageHeading title={t('patients.record.notFound.title')} body={t('patients.record.notFound.body')} /></div></div>;
  if (p.failure === 'forbidden') return <PatientsMessage pageHeading title={t('err.403')} body={t('registration.forbidden.body')} />;

  if (p.loading) return <p role="status">{t('registration.complete.loading')}</p>;

  const { register, control, formState: { errors } } = p.form;
  const f = { register, control, errors };
  return (
    <PageContainer width="reading" className="gap-5">
      {back}
      <PageHeader title={t('registration.complete.title')} description={t('registration.complete.intro')} />
      {p.status ? (
        <p role="status" className={cn('flex items-center gap-2 rounded-xl border bg-card p-4 font-bold', p.status === 'Complete' ? 'text-success-700' : 'text-warning-700')}>
          {p.status === 'Complete' ? <CircleCheck className="size-5 shrink-0" aria-hidden /> : <TriangleAlert className="size-5 shrink-0" aria-hidden />}
          {t(p.status === 'Complete' ? 'registration.complete.done.Complete' : 'registration.complete.done.Incomplete')}
        </p>
      ) : (
        <form noValidate onSubmit={e => { e.preventDefault(); p.onSubmit(); }} className="flex flex-col gap-5">
          <FormErrorSummary f={f} summary={p.errorSummary} country={p.country} values={p.values} quick={false} />
          <section aria-labelledby="complete-phones" className="flex flex-col gap-4 rounded-xl border bg-card p-4">
            <h2 id="complete-phones" className="text-base font-extrabold">{t('registration.complete.phones')}</h2>
            <p className="text-sm text-muted-foreground">{t('registration.complete.phones.note')}</p>
            <div className="grid gap-4 md:grid-cols-2">
              <TextField f={f} name="mobile" label={t('registration.field.mobile')} ltr type="tel" inputMode="tel" />
              <TextField f={f} name="alternatePhone" label={t('registration.field.alternatePhone')} ltr type="tel" inputMode="tel" />
              <TextField f={f} name="email" label={t('registration.field.email')} ltr type="email" inputMode="email" wide />
            </div>
          </section>
          <section aria-labelledby="complete-address" className="flex flex-col gap-4 rounded-xl border bg-card p-4">
            <h2 id="complete-address" className="text-base font-extrabold">{t('registration.complete.address')}</h2>
            <div className="grid gap-4 md:grid-cols-2"><AddressFields f={f} country={p.country} values={p.values} /></div>
          </section>
          <section aria-labelledby="complete-contact" className="flex flex-col gap-4 rounded-xl border bg-card p-4">
            <h2 id="complete-contact" className="text-base font-extrabold">{t('registration.complete.emergencyContact')}</h2>
            <div className="grid gap-4 md:grid-cols-2"><EmergencyContactFields f={f} country={p.country} required={false} /></div>
            <p className="text-sm text-muted-foreground">{t('registration.family.note')}</p>
          </section>
          <section aria-labelledby="complete-payer" className="flex flex-col gap-4 rounded-xl border bg-card p-4">
            <h2 id="complete-payer" className="text-base font-extrabold">{t('registration.complete.payer')}</h2>
            <PayerStep f={f} country={p.country} values={p.values} />
          </section>
          <div className="flex flex-col items-start gap-2">
            {p.failure === 'changed' && (
              <div role="alert" className="flex flex-col items-start gap-2 text-sm font-semibold text-danger-700">
                <p>{t('registration.complete.changed')}</p>
                <Button type="button" variant="outline" onClick={p.onReload}>{t('registration.complete.changed.reload')}</Button>
              </div>
            )}
            {p.failure === 'error' && <p role="alert" className="text-sm font-semibold text-danger-700">{t('registration.complete.failed')}</p>}
            <Button type="submit" loading={p.saving}>{p.saving ? t('registration.save.saving') : t('registration.complete.save')}</Button>
          </div>
        </form>
      )}
    </PageContainer>
  );
}
