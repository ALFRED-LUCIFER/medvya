import type { ReactNode } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { CircleAlert } from 'lucide-react';
import { useI18n, type Country } from '@medvya/i18n';
import { Button, Input, Label, PageHeader } from '@medvya/ui';
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
        <label key={c} className="flex min-h-11 items-center gap-2.5 rounded-lg border p-3 text-sm has-checked:border-2 has-checked:border-brand-700">
          <input type="radio" name="country" value={c} checked={country === c} onChange={() => onChange(c)} className="size-4 accent-brand-700" />
          <span className="flex flex-col">
            <span className="font-bold">{t(`onboarding.country.${c}`)}</span>
            <span className="text-muted-foreground">{t(`signin.region.${c}`)}</span>
          </span>
        </label>
      ))}
      <p className="text-xs text-muted-foreground">{t('onboarding.start.residency')}</p>
    </fieldset>
  );
}

type Field = 'practiceName' | 'city' | 'fullName' | 'email';

function TextField({ form, name, label, ltr, autoFocus }: { form: UseFormReturn<SignUpForm>; name: Field; label: string; ltr?: boolean; autoFocus?: boolean }) {
  const { t } = useI18n();
  const id = `signup-${name}`, error = form.formState.errors[name];
  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={id}>{label}</Label>
      <Input id={id} type={name === 'email' ? 'email' : 'text'} dir={ltr ? 'ltr' : undefined} spellCheck={false} autoFocus={autoFocus}
        autoComplete={name === 'email' ? 'email' : name === 'fullName' ? 'name' : 'off'}
        aria-invalid={error ? true : undefined} aria-describedby={error ? `${id}-error` : undefined} {...form.register(name)} />
      {error && (
        <p id={`${id}-error`} role="alert" className="flex items-center gap-1.5 text-sm font-semibold text-danger-700">
          <CircleAlert className="size-4 shrink-0" aria-hidden />{t(name === 'email' ? 'signin.email.invalid' : 'signin.create.required')}
        </p>
      )}
    </div>
  );
}

type SignUpProps = {
  languageSwitcher: ReactNode; form: UseFormReturn<SignUpForm>; step: 1 | 2; country: Country;
  onCountry: (c: Country) => void; onNext: () => void; onBack: () => void;
};

/**
 * F-03 create account, steps 1 and 2 (6 Nov design), from the region selector. Step 3 (securing the sign-in) is the provider's page;
 * then the setup wizard opens with these answers filled in.
 */
export default function SignUpPage({ languageSwitcher, form, step, country, onCountry, onNext, onBack }: SignUpProps) {
  const { t } = useI18n();
  const steps = (['signin.create.practice.title', 'signin.create.about.title', 'signin.create.secure'] as const)
    .map((key, i) => ({ label: t(key), state: i + 1 < step ? 'done' as const : i + 1 === step ? 'current' as const : 'todo' as const }));
  return (
    <AuthFrame languageSwitcher={languageSwitcher} steps={steps} country={country}>
      <form noValidate onSubmit={e => { e.preventDefault(); onNext(); }} className="flex flex-col gap-[18px]">
        {step === 1 ? <>
          {/* Wraps rather than truncates: nothing loads into this title, and on a phone it must be read whole. */}
          <PageHeader className="[&_h1]:whitespace-normal" title={t('signin.create.practice.title')} description={t('signin.create.practice.body')} />
          <CountryChoice country={country} onChange={onCountry} />
          <TextField form={form} name="practiceName" label={t('signin.create.practiceName')} />
          <TextField form={form} name="city" label={t(country === 'AE' ? 'signin.create.emirate' : 'signin.create.city')} />
          <Button type="submit" className="min-h-12">{t('signin.continue')}</Button>
        </> : <>
          <PageHeader className="[&_h1]:whitespace-normal" title={t('signin.create.about.title')} description={t('signin.create.about.body')} />
          <TextField form={form} name="fullName" label={t('signin.create.fullName')} autoFocus />
          <TextField form={form} name="email" label={t('signin.email')} ltr />
          <fieldset className="flex flex-col gap-2">
            <legend className="mb-1 text-sm font-bold">{t('signin.create.youAre')}</legend>
            <div className="grid grid-cols-2 gap-2">
              {(['doctor', 'manager'] as const).map(r => (
                <label key={r} className="flex min-h-11 items-center gap-2.5 rounded-lg border p-3 text-sm has-checked:border-2 has-checked:border-brand-700">
                  <input type="radio" value={r} className="size-4 accent-brand-700" {...form.register('role')} />
                  {t(`signin.create.${r}`)}
                </label>
              ))}
            </div>
          </fieldset>
          <div className="grid grid-cols-2 gap-3">
            <Button type="button" variant="outline" className="min-h-12" onClick={onBack}>{t('signin.create.back')}</Button>
            <Button type="submit" className="min-h-12">{t('signin.continue')}</Button>
          </div>
        </>}
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
