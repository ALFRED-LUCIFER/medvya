import { zodResolver } from '@hookform/resolvers/zod';
import { useI18n } from '@medvya/i18n';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { errorKey } from '../../api/domain/errors';
import { codeForm, emailForm, type CodeForm, type EmailForm, type Facility, type SignInStep, type Staff } from '../domain/session';
import { biometricsAvailable, listFacilities, startSignIn, unlockWithBiometrics, verifyCode } from '../services/session';
import { useSession } from './session';
import { useUnlockMethod } from './useUnlockMethod';

/** Staff sign-in (MDV-P5-034): work email, code, facility, then the offer to unlock with the phone's face or fingerprint unlock. */
export function useSignInController() {
  const { t } = useI18n();
  const { begin } = useSession();
  const unlockMethod = useUnlockMethod();
  const [step, setStep] = useState<SignInStep>('email');
  const [email, setEmail] = useState('');
  const [staff, setStaff] = useState<Staff | null>(null);
  const [facility, setFacility] = useState<Facility | null>(null);

  const emailFormApi = useForm<EmailForm>({ resolver: zodResolver(emailForm), defaultValues: { email: '' } });
  const codeFormApi = useForm<CodeForm>({ resolver: zodResolver(codeForm), defaultValues: { code: '' } });

  const start = useMutation({ mutationFn: startSignIn, onSuccess: (_, e) => { setEmail(e); setStep('code'); } });
  const verify = useMutation({
    mutationFn: (code: string) => verifyCode(email, code),
    onSuccess: s => {
      if (!s) { codeFormApi.setError('code', { message: 'mobile.signIn.codeWrong' }); return; }
      setStaff(s); setStep('facility');
    },
  });
  const facilities = useQuery({ queryKey: ['facilities', staff?.id], queryFn: () => listFacilities(staff!.id), enabled: !!staff });
  const canBiometric = useQuery({ queryKey: ['biometrics-available'], queryFn: biometricsAvailable, enabled: step === 'biometric' });
  const enable = useMutation({ mutationFn: () => unlockWithBiometrics(t('mobile.lock.prompt')), onSuccess: ok => { if (ok) finish(true); } });

  function finish(biometric: boolean) {
    if (staff && facility) begin({ staff, facility }, biometric);
  }
  const failure = start.error ?? verify.error ?? facilities.error;
  return {
    step,
    email,
    emailForm: emailFormApi,
    codeForm: codeFormApi,
    onSubmitEmail: () => void emailFormApi.handleSubmit(v => start.mutate(v.email))(),
    onSubmitCode: () => void codeFormApi.handleSubmit(v => verify.mutate(v.code))(),
    onBack: () => { setStep('email'); codeFormApi.reset(); verify.reset(); },
    busy: start.isPending || verify.isPending,
    facilities: facilities.data ?? [],
    facilitiesLoading: facilities.isPending && !!staff,
    onChooseFacility: (f: Facility) => { setFacility(f); setStep('biometric'); },
    biometricAvailable: canBiometric.data ?? false,
    unlockMethod,
    onEnableBiometric: () => enable.mutate(),
    onSkipBiometric: () => finish(false),
    error: failure ? errorKey(failure) : null,
  };
}
