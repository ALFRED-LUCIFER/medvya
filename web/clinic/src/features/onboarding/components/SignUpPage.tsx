import type { ReactNode } from 'react';
import { useI18n, type Country } from '@medvya/i18n';
import { Button } from '@medvya/ui';
import Frame from './OnboardingFrame';

const countries: Country[] = ['IN', 'AE'];

/** Choose where you practise (F-02's choice, carried here), then the setup: before sign-up and, with no draft yet, after it. */
export function CountryChoice({ country, onChange }: { country: Country; onChange: (c: Country) => void }) {
  const { t } = useI18n();
  return (
    <fieldset className="flex flex-col gap-2">
      <legend className="mb-1 text-sm font-bold">{t('onboarding.start.legend')}</legend>
      {countries.map(c => (
        <label key={c} className="flex min-h-11 items-center gap-2.5 rounded-lg border p-3 text-sm">
          <input type="radio" name="country" value={c} checked={country === c} onChange={() => onChange(c)} className="size-4 accent-brand-700" />
          {t(`onboarding.country.${c}`)}
        </label>
      ))}
      <p className="text-xs text-muted-foreground">{t('onboarding.start.residency')}</p>
    </fieldset>
  );
}

type SignUpProps = { languageSwitcher: ReactNode; country: Country; onCountry: (c: Country) => void; onStart: () => void };

/** Public entry from the region selector: create the account (multi-factor sign-in), then the six steps. */
export default function SignUpPage({ languageSwitcher, country, onCountry, onStart }: SignUpProps) {
  const { t } = useI18n();
  return (
    <Frame languageSwitcher={languageSwitcher}>
      <h1 className="text-2xl font-extrabold">{t('onboarding.signup.title')}</h1>
      <p className="text-muted-foreground">{t('onboarding.signup.body')}</p>
      <CountryChoice country={country} onChange={onCountry} />
      <Button onClick={onStart}>{t('onboarding.signup.cta')}</Button>
    </Frame>
  );
}

type StartProps = SignUpProps & { starting: boolean; failed: boolean };

/** Signed in with no draft yet: the same choice, then the draft is created for that country. */
export function StartPage({ languageSwitcher, country, onCountry, onStart, starting, failed }: StartProps) {
  const { t } = useI18n();
  return (
    <Frame languageSwitcher={languageSwitcher}>
      <h1 className="text-2xl font-extrabold">{t('onboarding.title')}</h1>
      <CountryChoice country={country} onChange={onCountry} />
      <Button onClick={onStart} loading={starting}>{t('onboarding.start.cta')}</Button>
      <p aria-live="assertive" className="text-sm font-semibold text-danger-700">{failed ? t('onboarding.start.failed') : ''}</p>
    </Frame>
  );
}
