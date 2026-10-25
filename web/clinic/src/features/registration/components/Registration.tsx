import { useEffect, useRef } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { Link } from 'react-router';
import { Check, Loader2, UserSearch } from 'lucide-react';
import { useI18n, type Country } from '@medvya/i18n';
import { Button, cn } from '@medvya/ui';
import PatientsMessage from '../../patients/components/PatientsMessage';
import {
  identifierInputs, steps, type DuplicateCandidate, type DuplicateDecision, type RegistrationForm, type RegistrationResult, type Step,
} from '../domain/registration';
import DuplicateReview from './DuplicateReview';
import RegistrationSidePanel from './RegistrationSidePanel';
import RegistrationSuccess from './RegistrationSuccess';
import { ContactStep, FamilyStep, IdentityStep, PayerStep, QuickFields, ReviewStep } from './RegistrationSteps';

type Props = {
  form: UseFormReturn<RegistrationForm>;
  country: Country;
  quick: boolean;
  values: RegistrationForm;
  /** yyyy-mm-dd on the desk's calendar. */
  today: string;
  ageInstead: boolean;
  /** Under 18 (null while unknown): a guardian is required. */
  minor: boolean | null;
  step: Step | null;
  reachedIndex: number;
  onGoTo: (s: Step) => void;
  onBack: () => void;
  onContinue: () => void;
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
  /** Links to the two modes ("Quick (busy desk)" / "Full registration"); the mode is in the URL, never patient data. */
  quickPath: string;
  fullPath: string;
};

/**
 * P-02 patient registration: full mode in steps (MDV-P1-013 India, MDV-P1-014 UAE) or quick mode (MDV-P1-015), with the duplicate
 * warning while typing and the decision before save (MDV-P1-016). Country rules follow the tenant.
 */
export default function Registration(p: Props) {
  const { t } = useI18n();
  const heading = useRef<HTMLHeadingElement>(null);
  const shownStep = useRef(p.step);
  useEffect(() => {
    // Moving between steps puts focus on the new step's heading, so keyboard and screen-reader users start at its top.
    if (shownStep.current !== p.step) heading.current?.focus();
    shownStep.current = p.step;
  }, [p.step]);

  if (p.result)
    return <RegistrationSuccess result={p.result} name={p.registeredName} actorName={p.actorName} onRegisterAnother={p.onRegisterAnother}
      recordPath={p.recordPath(p.result.patientId)} bookPath={p.bookPath} checkInPath={p.checkInPath} />;
  if (p.failure === 'forbidden') return <PatientsMessage title={t('err.403')} body={t('registration.forbidden.body')} />;

  const { register, control, formState: { errors } } = p.form;
  const f = { register, control, errors };
  const today = p.today;
  const unresolved = p.candidates.length > 0 && !p.resolution;
  const step = p.step;
  const atReview = p.quick || step === 'review';
  const firstId = identifierInputs(p.values, p.country, p.quick)[0] ?? null;
  const entry = {
    fullName: p.values.fullName, mobile: p.values.mobile, identifier: firstId,
    date: p.ageInstead ? t('registration.duplicates.age', { age: p.values.ageYears }) : p.values.dateOfBirth,
  };
  const modeLink = (quick: boolean) => cn('flex min-h-11 items-center rounded-md px-3 text-sm font-bold',
    p.quick === quick ? 'bg-card text-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground');

  return (
    <div className="flex max-w-[1180px] flex-col gap-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex flex-col gap-1">
          <h1 className="text-2xl font-extrabold">{t('registration.title')}</h1>
          <p className="text-muted-foreground">{t(p.quick ? 'registration.quick.intro' : `registration.full.intro.${p.country}`)}</p>
        </div>
        <nav aria-label={t('registration.mode.label')} className="flex gap-1 rounded-lg bg-muted p-1">
          <Link to={p.quickPath} aria-current={p.quick ? 'page' : undefined} className={modeLink(true)}>{t('registration.mode.quick')}</Link>
          <Link to={p.fullPath} aria-current={p.quick ? undefined : 'page'} className={modeLink(false)}>{t('registration.mode.full')}</Link>
        </nav>
      </div>

      <div className="grid items-start gap-5 lg:grid-cols-[minmax(0,1fr)_300px]">
        <form noValidate onSubmit={e => { e.preventDefault(); if (atReview) p.onSubmit(); else p.onContinue(); }} className="flex min-w-0 flex-col gap-5">
          {step && (
            <ol aria-label={t('registration.steps.label')} className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-5">
              {steps.map((s, i) => {
                const current = s === step;
                const done = i < steps.indexOf(step);
                return (
                  <li key={s}>
                    <button type="button" onClick={() => p.onGoTo(s)} disabled={i > p.reachedIndex} aria-current={current ? 'step' : undefined}
                      className={cn('flex min-h-11 w-full items-center gap-2 rounded-md border px-2 text-start text-sm disabled:opacity-60',
                        current ? 'border-brand-700 bg-accent font-bold' : 'bg-card')}>
                      <span aria-hidden className={cn('flex size-6 shrink-0 items-center justify-center rounded-full text-xs font-bold',
                        current ? 'bg-brand-700 text-white' : done ? 'bg-success-700 text-white' : 'bg-muted')}>{done ? <Check className="size-4" /> : i + 1}</span>
                      <span>{t(`registration.step.${s}`)}{done && <span className="sr-only">{t('registration.steps.done')}</span>}</span>
                    </button>
                  </li>
                );
              })}
            </ol>
          )}

          <section aria-labelledby="registration-step" className="flex flex-col gap-4 rounded-xl border bg-card p-4">
            <div className="flex flex-col gap-1">
              <h2 id="registration-step" ref={heading} tabIndex={-1} className="text-base font-extrabold outline-none">
                {t(step ? `registration.step.${step}.title` : 'registration.quick.section')}
              </h2>
              {step && <p className="text-sm text-muted-foreground">{t(`registration.step.${step}.intro.${p.country}`)}</p>}
            </div>
            {p.quick && <QuickFields f={f} country={p.country} ageInstead={p.ageInstead} minor={p.minor} />}
            {step === 'identity' && <IdentityStep f={f} country={p.country} ageInstead={p.ageInstead} values={p.values} today={today} />}
            {step === 'contact' && <ContactStep f={f} country={p.country} values={p.values} />}
            {step === 'family' && <FamilyStep f={f} country={p.country} minor={p.minor} />}
            {step === 'payer' && <PayerStep f={f} country={p.country} values={p.values} />}
            {step === 'review' && <ReviewStep country={p.country} values={p.values} ageInstead={p.ageInstead} onEdit={p.onGoTo} />}
          </section>

          <div aria-live="polite" className="flex flex-col gap-2">
            {p.checking && <p className="flex items-center gap-2 text-sm text-muted-foreground"><Loader2 className="size-4 animate-spin" aria-hidden />{t('registration.duplicates.checking')}</p>}
            {p.checkFailed && <p className="text-sm font-semibold text-warning-700">{t('registration.duplicates.checkFailed')}</p>}
            {unresolved && (
              <p className="flex items-center gap-2 rounded-md border bg-card px-3 py-2 text-sm font-bold text-warning-700">
                <UserSearch className="size-5 shrink-0" aria-hidden />
                {t(atReview ? 'registration.duplicates.inline' : 'registration.duplicates.inlineLater', { n: p.candidates.length })}
              </p>
            )}
          </div>

          {atReview && p.candidates.length > 0 && (
            <DuplicateReview candidates={p.candidates} resolution={p.resolution} onResolve={p.onResolve} onReconsider={p.onReconsider}
              entry={entry} recordPath={p.recordPath} />
          )}

          <div className="flex flex-col gap-2">
            {p.failure === 'error' && <p role="alert" className="text-sm font-semibold text-danger-700">{t('registration.saveFailed')}</p>}
            {p.duplicateUnresolved && unresolved && <p role="alert" className="text-sm font-semibold text-danger-700">{t('registration.duplicates.serverRefused')}</p>}
            <div className="flex flex-wrap gap-2">
              {step && step !== 'identity' && <Button type="button" variant="outline" onClick={p.onBack}>{t('registration.back')}</Button>}
              {atReview
                ? <Button type="submit" disabled={!p.canSave} loading={p.saving}>
                    {unresolved ? t('registration.save.resolveFirst') : p.saving ? t('registration.save.saving') : t('registration.save')}
                  </Button>
                : <Button type="submit">{t('registration.continue')}</Button>}
            </div>
          </div>
        </form>

        <RegistrationSidePanel country={p.country} values={p.values} ageInstead={p.ageInstead} step={step} today={today} />
      </div>
    </div>
  );
}
