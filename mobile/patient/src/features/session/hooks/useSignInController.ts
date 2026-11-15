import { zodResolver } from '@hookform/resolvers/zod';
import { useI18n } from '@medvya/i18n';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { errorKey } from '../../api/domain/errors';
import { codeForm, maskPhone, phoneForm, SignInWaitError, toE164, type Clinic, type CodeForm, type Person, type PhoneForm, type SignInStep } from '../domain/session';
import { biometricsAvailable, requestCode, unlockWithBiometrics, verifyCode, type Mobile } from '../services/session';
import { useSession } from './session';
import { useUnlockMethod } from './useUnlockMethod';

/**
 * Patient onboarding and sign-in (MDV-P5-033, MDV-P5-036): welcome, mobile number, SMS code, who is this for, then the phone's
 * unlock. With a server no records are linked yet, so there is no one to choose: the code leads straight to the unlock step.
 */
export function useSignInController() {
  const { t, country, locale } = useI18n();
  const { begin } = useSession();
  const unlockMethod = useUnlockMethod();
  const [step, setStep] = useState<SignInStep>('welcome');
  const [page, setPage] = useState(0);
  const [mobile, setMobile] = useState<Mobile | null>(null);
  const [found, setFound] = useState<{ people: Person[]; clinic: Clinic | null } | null>(null);
  const [person, setPerson] = useState<Person | null>(null);

  const phoneFormApi = useForm<PhoneForm>({ resolver: zodResolver(phoneForm), defaultValues: { country, number: '' } });
  const codeFormApi = useForm<CodeForm>({ resolver: zodResolver(codeForm), defaultValues: { code: '' } });

  const send = useMutation({ mutationFn: (m: Mobile) => requestCode(m, locale), onSuccess: (_, m) => { setMobile(m); setStep('code'); } });
  const verify = useMutation({
    mutationFn: (code: string) => verifyCode(mobile!, code),
    onSuccess: r => {
      if (!r) { codeFormApi.setError('code', { message: 'mobile.signIn.codeWrong' }); return; }
      setFound(r); setStep(r.clinic ? 'who' : 'biometric');
    },
  });
  const canBiometric = useQuery({ queryKey: ['biometrics-available'], queryFn: biometricsAvailable, enabled: step === 'biometric' });
  const enable = useMutation({ mutationFn: () => unlockWithBiometrics(t('mobile.lock.prompt')), onSuccess: ok => { if (ok) finish(true); } });

  function finish(biometric: boolean) {
    if (mobile && found && (person || !found.clinic)) begin({ person, phone: mobile.phone, clinic: found.clinic }, biometric);
  }
  const failure = send.error ?? verify.error;
  return {
    step,
    page,
    onPage: setPage,
    onStart: () => setStep('phone'),
    phoneForm: phoneFormApi,
    codeForm: codeFormApi,
    maskedPhone: mobile ? maskPhone(mobile.phone) : '',
    onSubmitPhone: () => void phoneFormApi.handleSubmit(v => send.mutate({ country: v.country, phone: toE164(v.country, v.number)! }))(),
    onSubmitCode: () => void codeFormApi.handleSubmit(v => verify.mutate(v.code))(),
    onResend: () => { if (mobile) send.mutate(mobile); },
    onBack: () => { setStep('phone'); codeFormApi.reset(); verify.reset(); },
    busy: send.isPending || verify.isPending,
    people: found?.people ?? [],
    onChoosePerson: (p: Person) => { setPerson(p); setStep('biometric'); },
    biometricAvailable: canBiometric.data ?? false,
    unlockMethod,
    onEnableBiometric: () => enable.mutate(),
    onSkipBiometric: () => finish(false),
    error: failure ? errorKey(failure) : null,
    waitMinutes: failure instanceof SignInWaitError ? failure.minutes : null,
  };
}
