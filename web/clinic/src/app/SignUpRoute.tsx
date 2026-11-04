import { useState, type ReactNode } from 'react';
import { useSearchParams } from 'react-router';
import { useI18n, type Country } from '@medvya/i18n';
import SignUpPage from '../features/onboarding/components/SignUpPage';
import { startSignUp } from '../features/onboarding/services/onboardingApi';

/** Public sign-up: the country comes from the region selector (?country=IN), then multi-factor sign-in creates the account. */
export default function SignUpRoute({ switcher }: { switcher: ReactNode }) {
  const { country: fallback } = useI18n();
  const [params] = useSearchParams();
  const asked = params.get('country');
  const [country, setCountry] = useState<Country>(asked === 'IN' || asked === 'AE' ? asked : fallback);
  return <SignUpPage languageSwitcher={switcher} country={country} onCountry={setCountry} onStart={() => startSignUp(country)} />;
}
