import { zodResolver } from '@hookform/resolvers/zod';
import { useI18n } from '@medvya/i18n';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { errorKey } from '../../api/domain/errors';
import { codeForm, maskPhone, phoneForm, toE164, type Clinic, type CodeForm, type Person, type PhoneForm, type SignInStep } from '../domain/session';
import { biometricsAvailable, requestCode, unlockWithBiometrics, verifyCode } from '../services/session';
import { useSession } from './session';

/** Patient onboarding and sign-in (MDV-P5-036): welcome, mobile number, SMS code, who is this for, then Face ID. */
export function useSignInController() {
  const { t, country } = useI18n();
  const { begin } = useSession();
  const [step, setStep] = useState<SignInStep>('welcome');
  const [page, setPage] = useState(0);
  const [phone, setPhone] = useState('');
  const [found, setFound] = useState<{ people: Person[]; clinic: Clinic } | null>(null);
  const [person, setPerson] = useState<Person | null>(null);

  const phoneFormApi = useForm<PhoneForm>({ resolver: zodResolver(phoneForm), defaultValues: { country, number: '' } });
  const codeFormApi = useForm<CodeForm>({ resolver: zodResolver(codeForm), defaultValues: { code: '' } });

  const send = useMutation({ mutationFn: requestCode, onSuccess: (_, p) => { setPhone(p); setStep('code'); } });
  const verify = useMutation({
    mutationFn: (code: string) => verifyCode(phone, code),
    onSuccess: r => {
      if (!r) { codeFormApi.setError('code', { message: 'mobile.signIn.codeWrong' }); return; }
      setFound(r); setStep('who');
    },
  });
  const canBiometric = useQuery({ queryKey: ['biometrics-available'], queryFn: biometricsAvailable, enabled: step === 'biometric' });
  const enable = useMutation({ mutationFn: () => unlockWithBiometrics(t('mobile.lock.prompt')), onSuccess: ok => { if (ok) finish(true); } });

  function finish(biometric: boolean) {
    if (person && found) begin({ person, phone, clinic: found.clinic }, biometric);
  }
  const failure = send.error ?? verify.error;
  return {
    step,
    page,
    onPage: setPage,
    onStart: () => setStep('phone'),
    phoneForm: phoneFormApi,
    codeForm: codeFormApi,
    maskedPhone: phone ? maskPhone(phone) : '',
    onSubmitPhone: () => void phoneFormApi.handleSubmit(v => send.mutate(toE164(v.country, v.number)!))(),
    onSubmitCode: () => void codeFormApi.handleSubmit(v => verify.mutate(v.code))(),
    onResend: () => send.mutate(phone),
    onBack: () => { setStep('phone'); codeFormApi.reset(); verify.reset(); },
    busy: send.isPending || verify.isPending,
    people: found?.people ?? [],
    onChoosePerson: (p: Person) => { setPerson(p); setStep('biometric'); },
    biometricAvailable: canBiometric.data ?? false,
    onEnableBiometric: () => enable.mutate(),
    onSkipBiometric: () => finish(false),
    error: failure ? errorKey(failure) : null,
  };
}
