import type { UseFormReturn } from 'react-hook-form';
import { Link } from 'react-router';
import { ArrowLeft, CircleCheck, TriangleAlert } from 'lucide-react';
import { useI18n, type Country } from '@medvya/i18n';
import { Button, buttonVariants, cn } from '@medvya/ui';
import type { RegistrationStatus } from '../../patients/domain/directory';
import PatientsMessage from '../../patients/components/PatientsMessage';
import type { RegistrationForm } from '../domain/registration';
import { AddressFields, EmergencyContactFields, PayerStep } from './RegistrationSteps';

type Props = {
  form: UseFormReturn<RegistrationForm>;
  country: Country;
  values: RegistrationForm;
  saving: boolean;
  onSubmit: () => void;
  /** Set after a save: Complete closes the desk's completion task. */
  status: RegistrationStatus | null;
  failure: 'notFound' | 'forbidden' | 'error' | null;
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
    return <div className="flex flex-col gap-4">{back}<div role="alert"><PatientsMessage title={t('patients.record.notFound.title')} body={t('patients.record.notFound.body')} /></div></div>;
  if (p.failure === 'forbidden') return <PatientsMessage title={t('err.403')} body={t('registration.forbidden.body')} />;

  const { register, control, formState: { errors } } = p.form;
  const f = { register, control, errors };
  return (
    <div className="flex max-w-[880px] flex-col gap-5">
      {back}
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-extrabold">{t('registration.complete.title')}</h1>
        <p className="text-muted-foreground">{t('registration.complete.intro')}</p>
      </div>
      {p.status ? (
        <p role="status" className={cn('flex items-center gap-2 rounded-xl border bg-card p-4 font-bold', p.status === 'Complete' ? 'text-success-700' : 'text-warning-700')}>
          {p.status === 'Complete' ? <CircleCheck className="size-5 shrink-0" aria-hidden /> : <TriangleAlert className="size-5 shrink-0" aria-hidden />}
          {t(p.status === 'Complete' ? 'registration.complete.done.Complete' : 'registration.complete.done.Incomplete')}
        </p>
      ) : (
        <form noValidate onSubmit={e => { e.preventDefault(); p.onSubmit(); }} className="flex flex-col gap-5">
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
            {p.failure === 'error' && <p role="alert" className="text-sm font-semibold text-danger-700">{t('registration.complete.failed')}</p>}
            <Button type="submit" loading={p.saving}>{p.saving ? t('registration.save.saving') : t('registration.complete.save')}</Button>
          </div>
        </form>
      )}
    </div>
  );
}
