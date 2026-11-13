import type { ReactNode } from 'react';
import { useSearchParams } from 'react-router';
import { useI18n } from '@medvya/i18n';
import SignUpPage from '../features/onboarding/components/SignUpPage';
import { useSignUpController } from '../features/onboarding/hooks/useSignUpController';

/** Public sign-up: the country comes from the region selector (?country=IN), then multi-factor sign-in creates the account. */
export default function SignUpRoute({ switcher }: { switcher: ReactNode }) {
  const { country: fallback } = useI18n();
  const asked = useSearchParams()[0].get('country');
  const vm = useSignUpController(asked === 'IN' || asked === 'AE' ? asked : fallback);
  return <SignUpPage languageSwitcher={switcher} form={vm.form} step={vm.step} country={vm.country} errorFocusKey={vm.errorFocusKey} onCountry={vm.chooseCountry}
    onNext={() => void vm.next()} onBack={vm.back} />;
}
