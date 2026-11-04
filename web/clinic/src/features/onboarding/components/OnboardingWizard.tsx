import type { ReactNode } from 'react';
import { Check } from 'lucide-react';
import { useI18n, type Country } from '@medvya/i18n';
import { Button } from '@medvya/ui';
import { stepIds, type FinishResult, type Rules, type WizardForm } from '../domain/onboarding';
import type { UseFormReturn } from 'react-hook-form';
import Frame from './OnboardingFrame';
import { ClinicStep, HoursStep, LicenceStep, ProfileStep, TeamStep, TemplatesStep } from './OnboardingSteps';

type Props = {
  languageSwitcher: ReactNode;
  form: UseFormReturn<WizardForm>;
  country: Country;
  rules: Rules;
  step: number;
  steps: number[];
  index: number;
  last: boolean;
  saveState: 'idle' | 'saving' | 'saved' | 'failed';
  problem: 'check' | 'finish' | null;
  working: boolean;
  done: FinishResult | null;
  homeLabel: 'today' | 'frontDesk';
  onNext: () => void;
  onBack: () => void;
  onHome: () => void;
};

/**
 * F-04 six-step onboarding. A form per step inside one frame: progress, the saved-state line and the side note. Server errors
 * appear under their field; a polite live region says that something needs fixing and that progress is saved.
 */
export default function OnboardingWizard({ languageSwitcher, form, country, rules, step, steps, index, last, saveState, problem, working, done, homeLabel, onNext, onBack, onHome }: Props) {
  const { t } = useI18n();
  const name = t(`onboarding.step.${stepIds[step - 1]!}`);
  const common = { form, country, rules };
  const body = [null, <ProfileStep key="1" {...common} />, <LicenceStep key="2" {...common} />, <ClinicStep key="3" {...common} />,
    <HoursStep key="4" {...common} />, <TemplatesStep key="5" {...common} />, <TeamStep key="6" {...common} />][step];

  if (done)
    return (
      <Frame languageSwitcher={languageSwitcher}>
        <div role="status" className="flex flex-col items-start gap-3">
          <h1 className="text-2xl font-extrabold">{t('onboarding.done.title')}</h1>
          <p>{t('onboarding.done.invitesFailed', { n: done.invitationsFailed })}</p>
          <Button onClick={onHome}>{t(`onboarding.finish.${homeLabel}`)}</Button>
        </div>
      </Frame>
    );

  return (
    <Frame languageSwitcher={languageSwitcher}>
      <header className="flex flex-col gap-2">
        <h1 className="text-2xl font-extrabold">{t('onboarding.title')}</h1>
        <p className="text-sm font-semibold text-muted-foreground">{t('onboarding.progress', { n: index + 1, total: steps.length, name })}</p>
        <nav aria-label={t('onboarding.steps.nav')}>
          <ol className="flex flex-wrap gap-2">
            {steps.map((s, i) => (
              <li key={s} aria-current={s === step ? 'step' : undefined}
                className={`flex min-h-8 items-center gap-1.5 rounded-full border px-3 text-xs font-semibold ${s === step ? 'bg-brand-700 text-white' : 'bg-card'}`}>
                {i < index && <Check className="size-3.5" aria-hidden />}
                {i < index && <span className="sr-only">{t('onboarding.stepDone')}</span>}{t(`onboarding.step.${stepIds[s - 1]!}`)}
              </li>
            ))}
          </ol>
        </nav>
      </header>
      <form noValidate onSubmit={e => { e.preventDefault(); onNext(); }} className="flex flex-col gap-6" aria-labelledby="onboarding-step">
        <h2 id="onboarding-step" className="text-lg font-bold">{name}</h2>
        {body}
        <div className="flex flex-wrap items-center gap-3">
          {index > 0 && <Button type="button" variant="outline" onClick={onBack}>{t('onboarding.back')}</Button>}
          <Button type="submit" loading={working}>{last ? t(`onboarding.finish.${homeLabel}`) : t('onboarding.continue')}</Button>
          <p aria-live="polite" className="text-sm text-muted-foreground">
            {saveState === 'saving' ? t('onboarding.saving') : saveState === 'saved' ? t('onboarding.saved') : saveState === 'failed' ? t('onboarding.saveFailed') : ''}
          </p>
        </div>
        <div aria-live="assertive" className="text-sm font-semibold text-danger-700">
          {problem === 'check' && <p>{t('onboarding.problem.check')}</p>}
          {problem === 'finish' && <p>{t('onboarding.problem.finish')}</p>}
        </div>
      </form>
      <p className="text-xs text-muted-foreground">{t('onboarding.side')}</p>
    </Frame>
  );
}
