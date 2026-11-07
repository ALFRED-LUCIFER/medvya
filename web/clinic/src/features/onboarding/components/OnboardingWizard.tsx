import type { ReactNode } from 'react';
import { useWatch, type UseFormReturn } from 'react-hook-form';
import { ArrowLeft, ArrowRight, Check, LifeBuoy } from 'lucide-react';
import { useI18n, type Country } from '@medvya/i18n';
import { Button } from '@medvya/ui';
import { preview, skippable, stepIds, type FinishResult, type Rules, type WizardForm } from '../domain/onboarding';
import BookingPreview from './BookingPreview';
import { HoursStep, PatientsStep, PaymentsStep, PracticeStep, TeamStep } from './OnboardingSteps';
import ReadyScreen from './ReadyScreen';

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
  problem: 'check' | 'finish' | 'later' | null;
  working: boolean;
  done: FinishResult | null;
  homeLabel: 'today' | 'frontDesk';
  onNext: () => void;
  onSkip: () => void;
  onBack: () => void;
  onGoTo: (step: number) => void;
  onLater: () => void;
  onInvite: () => void;
  onRemoveMember: (i: number) => void;
  onBook: () => void;
  onHome: () => void;
};

/**
 * F-04 set-up wizard, 6 Nov design: the steps on the start side, one form in the middle and the live booking-page preview on the
 * end side (wide screens only; the form is complete without it). Server errors appear under their field; a polite live region says
 * that progress is saved, an assertive one that something needs fixing.
 */
export default function OnboardingWizard(p: Props) {
  const { t } = useI18n();
  const { form, country, rules, step, steps, index, done } = p;
  const values = useWatch({ control: form.control }) as WizardForm;
  const id = stepIds[step - 1]!;
  const name = t(`onboarding.step.${id}`);
  const body = [null, <PracticeStep key="1" form={form} country={country} rules={rules} />, <HoursStep key="2" form={form} country={country} rules={rules} />,
    <TeamStep key="3" form={form} onInvite={p.onInvite} onRemove={p.onRemoveMember} />, <PatientsStep key="4" form={form} />, <PaymentsStep key="5" country={country} />][step];
  const progress = done ? 100 : (index / steps.length) * 100;

  return (
    <div className="flex min-h-screen flex-col bg-muted/40">
      <header className="flex flex-wrap items-center gap-3 border-b bg-card px-4 py-2 md:px-6">
        <span className="text-lg font-extrabold">{t('app.name')}</span>
        <span className="text-sm text-muted-foreground">{t('onboarding.title')}</span>
        <span className="flex-1" />
        {!done && <span className="text-sm font-semibold text-muted-foreground">{t('onboarding.stepOf', { n: index + 1, total: steps.length })}</span>}
        {!done && <Button type="button" variant="ghost" onClick={p.onLater} disabled={p.working}>{t('onboarding.later')}</Button>}
        {p.languageSwitcher}
      </header>
      <div aria-hidden className="h-1 bg-muted"><div className="h-full bg-brand-700 transition-[width]" style={{ width: `${progress}%` }} /></div>

      <div className="mx-auto grid w-full max-w-[1320px] flex-1 gap-6 p-4 md:p-8 min-[1024px]:grid-cols-[240px_minmax(0,1fr)] min-[1200px]:grid-cols-[240px_minmax(0,1fr)_300px]">
        <nav aria-label={t('onboarding.steps.nav')} className="hidden flex-col gap-4 lg:flex">
          <ol className="flex flex-col gap-1">
            {steps.map((s, i) => {
              const sid = stepIds[s - 1]!, past = done !== null || i < index, current = !done && s === step;
              return (
                <li key={s}>
                  <button type="button" disabled={done !== null || i > index} onClick={() => p.onGoTo(s)} aria-current={current ? 'step' : undefined}
                    className={`flex min-h-11 w-full items-start gap-3 rounded-lg px-3 py-2 text-start disabled:cursor-default ${current ? 'bg-card shadow-sm' : ''}`}>
                    <span className={`flex size-6 shrink-0 items-center justify-center rounded-full text-xs font-bold ${past ? 'bg-brand-700 text-white' : current ? 'bg-ink-900 text-white' : 'bg-muted text-muted-foreground'}`}>
                      {past ? <Check className="size-3.5" aria-hidden /> : s}
                    </span>
                    <span className="flex flex-col">
                      {past && <span className="sr-only">{t('onboarding.stepDone')}</span>}
                      <strong className="text-sm">{t(`onboarding.step.${sid}`)}</strong>
                      <span className="text-xs text-muted-foreground">{sid === 'payments' ? t(`onboarding.stepHint.payments.${country}`) : t(`onboarding.stepHint.${sid}`)}</span>
                    </span>
                  </button>
                </li>
              );
            })}
          </ol>
          <p className="flex flex-col gap-1 rounded-xl border bg-card p-4 text-xs text-muted-foreground">
            <strong className="flex items-center gap-1.5 text-sm text-foreground"><LifeBuoy className="size-4" aria-hidden />{t('onboarding.help.title')}</strong>
            {t('onboarding.help.body')}
          </p>
        </nav>

        <main className="flex flex-col gap-6 rounded-2xl border bg-card p-5 md:p-8">
          {done ? <ReadyScreen form={values} done={done} homeLabel={p.homeLabel} onBook={p.onBook} onHome={p.onHome} /> : (
            <form noValidate onSubmit={e => { e.preventDefault(); p.onNext(); }} className="flex flex-col gap-6" aria-labelledby="onboarding-step">
              <div className="flex flex-col gap-2">
                <p className="text-xs font-bold text-brand-700">{t('onboarding.eyebrow', { n: index + 1, name })}</p>
                <h1 id="onboarding-step" className="text-2xl font-extrabold">{t(`onboarding.${id}.title`)}</h1>
                <p className="text-muted-foreground">{t(`onboarding.${id}.intro`)}</p>
              </div>
              {body}
              <div className="flex flex-wrap items-center gap-3 border-t pt-4">
                {index > 0 && <Button type="button" variant="outline" onClick={p.onBack}><ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />{t('onboarding.back')}</Button>}
                <p aria-live="polite" className="text-sm text-muted-foreground">
                  {p.saveState === 'saving' ? t('onboarding.saving') : p.saveState === 'saved' ? t('onboarding.saved') : p.saveState === 'failed' ? t('onboarding.saveFailed') : ''}
                </p>
                <span className="flex-1" />
                {skippable(step) && <Button type="button" variant="ghost" onClick={p.onSkip} disabled={p.working}>{t('onboarding.skip')}</Button>}
                <Button type="submit" loading={p.working}>
                  {p.last ? t('onboarding.finishSetup') : t('onboarding.continue')}<ArrowRight className="size-4 rtl:rotate-180" aria-hidden />
                </Button>
              </div>
              <div aria-live="assertive" className="text-sm font-semibold text-danger-700">
                {p.problem && <p>{t(`onboarding.problem.${p.problem}`)}</p>}
              </div>
            </form>
          )}
        </main>

        {!done && (
          <aside aria-label={t('onboarding.preview.label')} className="hidden min-[1200px]:block">
            <BookingPreview view={preview(values)} mode={values.mode} currency={rules.currency} />
          </aside>
        )}
      </div>
    </div>
  );
}
