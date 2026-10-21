import type { ReactNode } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { CircleAlert, Loader2, UserSearch } from 'lucide-react';
import { useI18n, type Country } from '@medvya/i18n';
import { Button, Input, Label } from '@medvya/ui';
import PatientsMessage from '../../patients/components/PatientsMessage';
import { errorCode, quickIdentifier, sexes, type DuplicateCandidate, type DuplicateDecision, type QuickForm, type RegistrationResult } from '../domain/registration';
import DuplicateReview from './DuplicateReview';
import RegistrationSuccess from './RegistrationSuccess';

type Props = {
  form: UseFormReturn<QuickForm>;
  country: Country;
  ageInstead: boolean;
  checking: boolean;
  checkFailed: boolean;
  candidates: DuplicateCandidate[];
  resolution: DuplicateDecision | null;
  onResolve: (decision: DuplicateDecision) => void;
  onReconsider: () => void;
  canSave: boolean;
  saving: boolean;
  onSubmit: () => void;
  failure: 'forbidden' | 'error' | null;
  duplicateUnresolved: boolean;
  result: RegistrationResult | null;
  registeredName: string;
  actorName: string;
  onRegisterAnother: () => void;
  recordPath: (id: string) => string;
  bookPath: string | null;
  checkInPath: string | null;
};

/** P-02 quick mode (MDV-P1-015) with the duplicate warning while typing and the decision before save (MDV-P1-016). */
export default function QuickRegistration(p: Props) {
  const { t } = useI18n();
  if (p.result)
    return <RegistrationSuccess result={p.result} name={p.registeredName} actorName={p.actorName} onRegisterAnother={p.onRegisterAnother}
      recordPath={p.recordPath(p.result.patientId)} bookPath={p.bookPath} checkInPath={p.checkInPath} />;
  if (p.failure === 'forbidden') return <PatientsMessage title={t('err.403')} body={t('registration.forbidden.body')} />;

  const { register, formState: { errors } } = p.form;
  const scheme = quickIdentifier[p.country].scheme;
  const unresolved = p.candidates.length > 0 && !p.resolution;
  return (
    <div className="flex max-w-[880px] flex-col gap-5">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-extrabold">{t('registration.title')}</h1>
        <p className="text-muted-foreground">{t('registration.quick.intro')}</p>
      </div>

      <form noValidate onSubmit={e => { e.preventDefault(); p.onSubmit(); }} className="flex flex-col gap-5">
        <section aria-labelledby="quick-identity" className="flex flex-col gap-4 rounded-xl border bg-card p-4">
          <h2 id="quick-identity" className="text-base font-extrabold">{t('registration.quick.section')}</h2>
          <div className="grid gap-4 md:grid-cols-2">
            <Field id="fullName" label={t(p.country === 'AE' ? 'registration.field.fullName.AE' : 'registration.field.fullName')} required error={errors.fullName?.message}>
              {a => <Input {...a} {...register('fullName')} autoComplete="off" />}
            </Field>
            <Field id="mobile" label={t('registration.field.mobile')} required error={errors.mobile?.message} hint={t('registration.hint.mobile')}>
              {a => <Input {...a} {...register('mobile')} type="tel" inputMode="tel" dir="ltr" autoComplete="off" />}
            </Field>

            {p.ageInstead
              ? <Field id="ageYears" label={t('registration.field.ageYears')} required error={errors.ageYears?.message} hint={t('registration.hint.ageYears')}>
                  {a => <Input {...a} {...register('ageYears')} inputMode="numeric" dir="ltr" />}
                </Field>
              : <Field id="dateOfBirth" label={t('registration.field.dateOfBirth')} required error={errors.dateOfBirth?.message}>
                  {a => <Input {...a} {...register('dateOfBirth')} type="date" dir="ltr" />}
                </Field>}

            <fieldset aria-describedby={errors.sexAtBirth ? 'sexAtBirth-error' : undefined} className="flex flex-col gap-2">
              <legend className="mb-2 text-sm font-bold">{t('registration.field.sexAtBirth')} <Required /></legend>
              <div className="flex flex-wrap gap-2">
                {sexes.map(s => (
                  <label key={s} className="flex min-h-11 cursor-pointer items-center gap-2 rounded-md border border-input px-3 has-[:checked]:border-brand-700 has-[:checked]:bg-accent">
                    <input type="radio" value={s} {...register('sexAtBirth')} aria-invalid={errors.sexAtBirth ? true : undefined} className="size-4 accent-brand-700" />
                    {t(`patients.sex.${s}`)}
                  </label>
                ))}
              </div>
              <FieldError id="sexAtBirth-error" code={errors.sexAtBirth?.message} />
            </fieldset>

            <Field id="identifier" label={t(`registration.field.identifier.${scheme}`)} required={quickIdentifier[p.country].required}
              error={errors.identifier?.message} hint={t(`registration.hint.identifier.${scheme}`)}>
              {a => <Input {...a} {...register('identifier')} dir="ltr" autoComplete="off" spellCheck={false} />}
            </Field>
          </div>
          {p.country === 'IN' && (
            <label className="flex min-h-11 cursor-pointer items-center gap-2 self-start">
              <input type="checkbox" {...register('ageInstead')} className="size-4 accent-brand-700" />
              {t('registration.field.ageInstead')}
            </label>
          )}
        </section>

        <div aria-live="polite" className="flex flex-col gap-2">
          {p.checking && <p className="flex items-center gap-2 text-sm text-muted-foreground"><Loader2 className="size-4 animate-spin" aria-hidden />{t('registration.duplicates.checking')}</p>}
          {p.checkFailed && <p className="text-sm font-semibold text-warning-700">{t('registration.duplicates.checkFailed')}</p>}
          {unresolved && (
            <p className="flex items-center gap-2 rounded-md border border-line-strong bg-card px-3 py-2 text-sm font-bold text-warning-700">
              <UserSearch className="size-5 shrink-0" aria-hidden />{t('registration.duplicates.inline', { n: p.candidates.length })}
            </p>
          )}
        </div>

        {p.candidates.length > 0 && (
          <DuplicateReview candidates={p.candidates} resolution={p.resolution} onResolve={p.onResolve} onReconsider={p.onReconsider}
            typed={p.form.getValues()} ageInstead={p.ageInstead} scheme={scheme} recordPath={p.recordPath} />
        )}

        <div className="flex flex-col items-start gap-2">
          {p.failure === 'error' && <p role="alert" className="text-sm font-semibold text-danger-700">{t('registration.saveFailed')}</p>}
          {p.duplicateUnresolved && unresolved && <p role="alert" className="text-sm font-semibold text-danger-700">{t('registration.duplicates.serverRefused')}</p>}
          <Button type="submit" disabled={!p.canSave} loading={p.saving}>
            {unresolved ? t('registration.save.resolveFirst') : p.saving ? t('registration.save.saving') : t('registration.save')}
          </Button>
        </div>
      </form>
    </div>
  );
}

const Required = () => {
  const { t } = useI18n();
  return <span className="text-danger-700" aria-hidden>{t('registration.requiredMark')}</span>;
};

function FieldError({ id, code }: { id: string; code: string | undefined }) {
  const { t } = useI18n();
  if (!code) return null;
  return (
    <p id={id} className="flex items-center gap-1.5 text-sm font-semibold text-danger-700">
      <CircleAlert className="size-4 shrink-0" aria-hidden />{t(`registration.error.${errorCode(code)}`)}
    </p>
  );
}

type InputA11y = { id: string; 'aria-invalid'?: true; 'aria-describedby'?: string; 'aria-required'?: true; className: string };

function Field({ id, label, required = false, error, hint, children }: {
  id: string; label: string; required?: boolean; error?: string; hint?: string; children: (a: InputA11y) => ReactNode;
}) {
  const described = [hint && `${id}-hint`, error && `${id}-error`].filter(Boolean).join(' ') || undefined;
  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={id}>{label} {required && <Required />}</Label>
      {children({ id, 'aria-invalid': error ? true : undefined, 'aria-describedby': described, 'aria-required': required || undefined, className: 'min-h-11' })}
      {hint && <p id={`${id}-hint`} className="text-xs text-muted-foreground">{hint}</p>}
      <FieldError id={`${id}-error`} code={error} />
    </div>
  );
}
