import type { ReactNode } from 'react';
import { useSearchParams } from 'react-router';
import { useI18n, type Country } from '@medvya/i18n';
import OnboardingWizard from '../features/onboarding/components/OnboardingWizard';
import { StartPage } from '../features/onboarding/components/SignUpPage';
import { useOnboardingController } from '../features/onboarding/hooks/useOnboardingController';
import type { FinishResult } from '../features/onboarding/domain/onboarding';
import ShellMessage from '../features/shell/components/ShellMessage';

const countryOf = (value: string | null, fallback: Country): Country => (value === 'IN' || value === 'AE' ? value : fallback);

type Props = { switcher: ReactNode; onFinished: (result: FinishResult, to?: string) => void; onLater: () => void; schedulePath: string };

/** F-04: the signed-in person's set-up wizard. Opening the app hands the new membership to the shell, which scopes the session to it. */
export default function OnboardingRoute({ switcher, onFinished, onLater, schedulePath }: Props) {
  const { t, country: fallback } = useI18n();
  const [params] = useSearchParams();
  const vm = useOnboardingController(countryOf(params.get('country'), fallback), onFinished, onLater);

  if (vm.loading) return <ShellMessage standalone busy title={t('onboarding.loading')} />;
  if (vm.loadFailed) return <ShellMessage standalone title={t('onboarding.loadFailed.title')} body={t('onboarding.loadFailed.body')} />;
  if (!vm.started || !vm.rules)
    return <StartPage languageSwitcher={switcher} country={vm.country} onCountry={vm.chooseCountry} onStart={vm.start} starting={vm.starting} failed={vm.startFailed} />;
  return (
    <OnboardingWizard languageSwitcher={switcher} form={vm.form} country={vm.country} rules={vm.rules} step={vm.step} steps={vm.steps} index={vm.index}
      last={vm.last} saveState={vm.saveState} problem={vm.problem} working={vm.working} done={vm.done}
      homeLabel={vm.form.getValues('mode') === 'Solo' ? 'today' : 'frontDesk'} onNext={() => void vm.next()} onSkip={vm.skip} onBack={vm.back} onGoTo={vm.goTo}
      onLater={() => void vm.later()} onInvite={vm.invite} onRemoveMember={vm.removeMember} onBook={() => vm.open(schedulePath)} onHome={() => vm.open()} />
  );
}
