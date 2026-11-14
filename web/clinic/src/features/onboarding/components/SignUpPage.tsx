import type { ReactNode } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { useI18n, type Country } from '@medvya/i18n';
import { Button, ChoiceRow, ErrorSummary, Field, FieldMarks, Input, PageHeader } from '@medvya/ui';
import AuthFrame from '../../session/components/AuthFrame';
import type { SignUpForm } from '../domain/signUp';
import Frame from './OnboardingFrame';

const countries: Country[] = ['IN', 'AE'];

/** Choose where you practise (F-02's choice, carried here), then the setup: before sign-up and, with no draft yet, after it. */
export function CountryChoice({ country, onChange }: { country: Country; onChange: (c: Country) => void }) {
  const { t } = useI18n();
  return (
    <fieldset className="flex flex-col gap-2">
      <legend className="mb-1 text-sm font-bold">{t('onboarding.start.legend')}</legend>
      {countries.map(c => (
        <ChoiceRow key={c} className="gap-2.5 rounded-lg border p-3 text-sm has-checked:border-2 has-checked:border-brand-700" type="radio" name="country" value={c} checked={country === c} onChange={() => onChange(c)}>
          <span className="flex flex-col">
            <span className="font-bold">{t(`onboarding.country.${c}`)}</span>
            <span className="text-muted-foreground">{t(`signin.region.${c}`)}</span>
          </span>
        </ChoiceRow>
      ))}
      <p className="text-xs text-muted-foreground">{t('onboarding.start.residency')}</p>
    </fieldset>
  );
}

type Name = 'practiceName' | 'city' | 'fullName' | 'email';

/** Every field here is required, so none carries a mark (FieldMarks "optional": only optional fields would). */
function TextField({ form, name, label, ltr, autoFocus }: { form: UseFormReturn<SignUpForm>; name: Name; label: string; ltr?: boolean; autoFocus?: boolean }) {
  const { t } = useI18n();
  const error = form.formState.errors[name] && t(name === 'email' ? 'signin.email.invalid' : 'signin.create.required');
  return (
    <Field id={`signup-${name}`} label={label} required error={error}>
      <Input type={name === 'email' ? 'email' : 'text'} dir={ltr ? 'ltr' : undefined} spellCheck={false} autoFocus={autoFocus}
        autoComplete={name === 'email' ? 'email' : name === 'fullName' ? 'name' : 'off'} {...form.register(name)} />
    </Field>
  );
}

type SignUpProps = {
  languageSwitcher: ReactNode; form: UseFormReturn<SignUpForm>; step: 1 | 2; country: Country;
  /** Changes once per failed Continue: the error summary takes focus then. */
  errorFocusKey: string;
  onCountry: (c: Country) => void; onNext: () => void; onBack: () => void;
};

/**
 * F-03 create account, steps 1 and 2 (6 Nov design), from the region selector. Step 3 (securing the sign-in) is the provider's page;
 * then the setup wizard opens with these answers filled in.
 */
export default function SignUpPage({ languageSwitcher, form, step, country, errorFocusKey, onCountry, onNext, onBack }: SignUpProps) {
  const { t } = useI18n();
  const labels: Record<Name, string> = {
    practiceName: t('signin.create.practiceName'), city: t(country === 'AE' ? 'signin.create.emirate' : 'signin.create.city'),
    fullName: t('signin.create.fullName'), email: t('signin.email'),
  };
  const errors = (step === 1 ? (['practiceName', 'city'] as const) : (['fullName', 'email'] as const)).flatMap(name => form.formState.errors[name]
    ? [{ id: `signup-${name}`, label: labels[name], message: t(name === 'email' ? 'signin.email.invalid' : 'signin.create.required') }] : []);
  const steps = (['signin.create.practice.title', 'signin.create.about.title', 'signin.create.secure'] as const)
    .map((key, i) => ({ label: t(key), state: i + 1 < step ? 'done' as const : i + 1 === step ? 'current' as const : 'todo' as const }));
  return (
    <AuthFrame languageSwitcher={languageSwitcher} steps={steps} country={country}>
      <form noValidate onSubmit={e => { e.preventDefault(); onNext(); }} className="flex flex-col gap-[18px]">
        <ErrorSummary title={t('field.errorSummary')} focusKey={errorFocusKey} errors={errors} />
        <FieldMarks mark="optional">
        {step === 1 ? <>
          {/* Wraps rather than truncates: nothing loads into this title, and on a phone it must be read whole. */}
          <PageHeader className="[&_h1]:whitespace-normal" title={t('signin.create.practice.title')} description={t('signin.create.practice.body')} />
          <CountryChoice country={country} onChange={onCountry} />
          <TextField form={form} name="practiceName" label={labels.practiceName} />
          <TextField form={form} name="city" label={labels.city} />
          <Button type="submit" className="min-h-12">{t('signin.continue')}</Button>
        </> : <>
          <PageHeader className="[&_h1]:whitespace-normal" title={t('signin.create.about.title')} description={t('signin.create.about.body')} />
          <TextField form={form} name="fullName" label={labels.fullName} autoFocus />
          <TextField form={form} name="email" label={labels.email} ltr />
          <fieldset className="flex flex-col gap-2">
            <legend className="mb-1 text-sm font-bold">{t('signin.create.youAre')}</legend>
            <div className="grid grid-cols-2 gap-2">
              {(['doctor', 'manager'] as const).map(r => (
                <ChoiceRow key={r} className="gap-2.5 rounded-lg border p-3 text-sm has-checked:border-2 has-checked:border-brand-700" type="radio" value={r} {...form.register('role')}>
                  {t(`signin.create.${r}`)}
                </ChoiceRow>
              ))}
            </div>
          </fieldset>
          <div className="grid grid-cols-2 gap-3">
            <Button type="button" variant="outline" className="min-h-12" onClick={onBack}>{t('signin.create.back')}</Button>
            <Button type="submit" className="min-h-12">{t('signin.continue')}</Button>
          </div>
        </>}
        </FieldMarks>
      </form>
    </AuthFrame>
  );
}

type StartProps = { languageSwitcher: ReactNode; country: Country; onCountry: (c: Country) => void; onStart: () => void; starting: boolean; failed: boolean };

/** Signed in with no draft yet: the same choice, then the draft is created for that country. */
export function StartPage({ languageSwitcher, country, onCountry, onStart, starting, failed }: StartProps) {
  const { t } = useI18n();
  return (
    <Frame languageSwitcher={languageSwitcher}>
      <PageHeader className="[&_h1]:whitespace-normal" title={t('onboarding.title')} />
      <CountryChoice country={country} onChange={onCountry} />
      <Button onClick={onStart} loading={starting}>{t('onboarding.start.cta')}</Button>
      <p aria-live="assertive" className="text-sm font-semibold text-danger-700">{failed ? t('onboarding.start.failed') : ''}</p>
    </Frame>
  );
}
