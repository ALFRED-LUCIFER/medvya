import { zodResolver } from '@hookform/resolvers/zod';
import { useI18n } from '@medvya/i18n';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { errorKey } from '../../api/domain/errors';
import { codeForm, emailForm, newPinForm, type CodeForm, type EmailForm, type Facility, type NewPinForm, type Session, type SignInStep, type Staff } from '../domain/session';
import { biometricsAvailable, chooseFacility, listFacilities, setPin, signInWithBrowser, signsInThroughServer, startSignIn, unlockWithBiometrics, verifyCode } from '../services/session';
import { useSession } from './session';
import { useUnlockMethod } from './useUnlockMethod';

/**
 * Staff sign-in (MDV-P5-034): the server's sign-in in the system browser (or, in a Development build without a server, the
 * cast's work email and code), facility, the app PIN (the lock's fallback), then the offer of the phone's face or fingerprint unlock.
 */
export function useSignInController() {
  const { t } = useI18n();
  const { begin } = useSession();
  const unlockMethod = useUnlockMethod();
  const [step, setStep] = useState<SignInStep>(() => (signsInThroughServer() ? 'browser' : 'email'));
  const [email, setEmail] = useState('');
  const [staff, setStaff] = useState<Staff | null>(null);
  const [chosen, setChosen] = useState<Session | null>(null);

  const emailFormApi = useForm<EmailForm>({ resolver: zodResolver(emailForm), defaultValues: { email: '' } });
  const codeFormApi = useForm<CodeForm>({ resolver: zodResolver(codeForm), defaultValues: { code: '' } });
  const pinFormApi = useForm<NewPinForm>({ resolver: zodResolver(newPinForm), defaultValues: { pin: '', confirm: '' } });

  const start = useMutation({ mutationFn: startSignIn, onSuccess: (_, e) => { setEmail(e); setStep('code'); } });
  const verify = useMutation({
    mutationFn: (code: string) => verifyCode(email, code),
    onSuccess: s => {
      if (!s) { codeFormApi.setError('code', { message: 'mobile.signIn.codeWrong' }); return; }
      setStaff(s); setStep('facility');
    },
  });
  const browser = useMutation({ mutationFn: signInWithBrowser, onSuccess: s => { if (s) { setStaff(s); setStep('facility'); } } });
  const choose = useMutation({ mutationFn: (f: Facility) => chooseFacility(staff!, f), onSuccess: s => { setChosen(s); setStep('pin'); } });
  const facilities = useQuery({ queryKey: ['facilities', staff?.id], queryFn: () => listFacilities(staff!.id), enabled: !!staff });
  const canBiometric = useQuery({ queryKey: ['biometrics-available'], queryFn: biometricsAvailable, enabled: step === 'biometric' });
  const savePin = useMutation({ mutationFn: setPin, onSuccess: () => { pinFormApi.reset(); setStep('biometric'); } });
  const enable = useMutation({ mutationFn: () => unlockWithBiometrics(t('mobile.lock.prompt'), t('mobile.pin.use')), onSuccess: ok => { if (ok) finish(true); } });

  function finish(biometric: boolean) {
    if (chosen) begin(chosen, biometric);
  }
  const failure = browser.error ?? start.error ?? verify.error ?? facilities.error ?? choose.error ?? savePin.error;
  return {
    step,
    email,
    emailForm: emailFormApi,
    codeForm: codeFormApi,
    onBrowserSignIn: () => browser.mutate(),
    onSubmitEmail: () => void emailFormApi.handleSubmit(v => start.mutate(v.email))(),
    onSubmitCode: () => void codeFormApi.handleSubmit(v => verify.mutate(v.code))(),
    onBack: () => { setStep('email'); codeFormApi.reset(); verify.reset(); },
    pinForm: pinFormApi,
    onSubmitPin: () => void pinFormApi.handleSubmit(v => savePin.mutate(v.pin))(),
    busy: browser.isPending || start.isPending || verify.isPending || choose.isPending || savePin.isPending,
    facilities: facilities.data ?? [],
    facilitiesLoading: facilities.isPending && !!staff,
    onChooseFacility: (f: Facility) => { if (!choose.isPending) choose.mutate(f); },
    biometricAvailable: canBiometric.data ?? false,
    unlockMethod,
    onEnableBiometric: () => enable.mutate(),
    onSkipBiometric: () => finish(false),
    error: failure ? errorKey(failure) : null,
  };
}
