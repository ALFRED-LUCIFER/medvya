import type { UseFormReturn } from 'react-hook-form';
import { Link } from 'react-router';
import { ArrowLeft, CircleCheck, TriangleAlert } from 'lucide-react';
import { useI18n, type Country } from '@medvya/i18n';
import { Button, buttonVariants, Card, cn, PageContainer, PageHeader, PageMessage } from '@medvya/ui';
import type { RegistrationStatus } from '../../patients/domain/directory';
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
  errorSummary: { focusKey: string; fields: FormField[] };
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
    <Link to={p.recordPath} className={cn(buttonVariants({ variant: 'ghost' }), 'px-2')}>
      <ArrowLeft className="rtl:rotate-180" aria-hidden />{t('registration.complete.back')}
    </Link>
  );
  // Announced: it answers the save (another facility's patient, or an old link); nothing was changed.
  // One header in every state, so the title and the way back never move (MDV-P2-059).
  const header = <PageHeader back={back} title={t('registration.complete.title')} description={t('registration.complete.intro')} />;
  if (p.failure === 'notFound')
    return <PageContainer width="reading">{header}<PageMessage kind="error" title={t('patients.record.notFound.title')}>{t('patients.record.notFound.body')}</PageMessage></PageContainer>;
  if (p.failure === 'forbidden')
    return <PageContainer width="reading">{header}<PageMessage kind="denied" title={t('err.403')}>{t('registration.forbidden.body')}</PageMessage></PageContainer>;
  if (p.loading) return <PageContainer width="reading">{header}<PageMessage kind="busy">{t('registration.complete.loading')}</PageMessage></PageContainer>;

  const { register, control, formState: { errors } } = p.form;
  const f = { register, control, errors };
  return (
    <PageContainer width="reading" className="gap-5">
      {header}
      {p.status ? (
        <Card role="status" className={cn('flex-row items-center gap-2 font-bold', p.status === 'Complete' ? 'text-success-700' : 'text-warning-700')}>
          {p.status === 'Complete' ? <CircleCheck className="size-5 shrink-0" aria-hidden /> : <TriangleAlert className="size-5 shrink-0" aria-hidden />}
          {t(p.status === 'Complete' ? 'registration.complete.done.Complete' : 'registration.complete.done.Incomplete')}
        </Card>
      ) : (
        <form noValidate onSubmit={e => { e.preventDefault(); p.onSubmit(); }} className="flex flex-col gap-5">
          <FormErrorSummary f={f} summary={p.errorSummary} country={p.country} values={p.values} quick={false} />
          <Card title={t('registration.complete.phones')} intro={t('registration.complete.phones.note')}>
            <div className="grid gap-4 md:grid-cols-2">
              <TextField f={f} name="mobile" label={t('registration.field.mobile')} ltr type="tel" inputMode="tel" />
              <TextField f={f} name="alternatePhone" label={t('registration.field.alternatePhone')} ltr type="tel" inputMode="tel" />
              <TextField f={f} name="email" label={t('registration.field.email')} ltr type="email" inputMode="email" wide />
            </div>
          </Card>
          <Card title={t('registration.complete.address')}>
            <div className="grid gap-4 md:grid-cols-2"><AddressFields f={f} country={p.country} values={p.values} /></div>
          </Card>
          <Card title={t('registration.complete.emergencyContact')}>
            <div className="grid gap-4 md:grid-cols-2"><EmergencyContactFields f={f} country={p.country} required={false} /></div>
            <p className="text-sm text-muted-foreground">{t('registration.family.note')}</p>
          </Card>
          <Card title={t('registration.complete.payer')}>
            <PayerStep f={f} country={p.country} values={p.values} />
          </Card>
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
