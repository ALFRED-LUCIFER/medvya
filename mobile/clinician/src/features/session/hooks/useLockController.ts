import { zodResolver } from '@hookform/resolvers/zod';
import { useI18n } from '@medvya/i18n';
import { useMutation } from '@tanstack/react-query';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { errorKey } from '../../api/domain/errors';
import { offersPin, pinForm, pinTriesLeft, type PinForm } from '../domain/session';
import { checkPin, countPinTry, resetPinTries, unlockWithBiometrics } from '../services/session';
import { useSignedIn } from './session';
import { useUnlockMethod } from './useUnlockMethod';

/**
 * The lock screen shows nothing of the record: who is signed in and how to unlock. Face or fingerprint first; the app
 * PIN when that is off or fails; after 5 wrong PINs the app signs out and wipes the phone. Switch user is a full sign-out.
 */
export function useLockController() {
  const { t } = useI18n();
  const { session, biometric, unlock, signOut } = useSignedIn();
  const unlockMethod = useUnlockMethod();
  const [wrong, setWrong] = useState(0);
  const form = useForm<PinForm>({ resolver: zodResolver(pinForm), defaultValues: { pin: '' } });
  // Both checks run on the phone: 'always', or TanStack pauses them offline and the lock never opens.
  const attempt = useMutation({
    mutationFn: async () => {
      const ok = await unlockWithBiometrics(t('mobile.lock.prompt'), t('mobile.pin.use'));
      if (ok) await resetPinTries();
      return ok;
    },
    networkMode: 'always',
    onSuccess: ok => { if (ok) unlock(); },
  });
  const check = useMutation({
    // The try is stored before the check, so neither a relaunch nor killing the app mid-check gives back a guess.
    mutationFn: async (pin: string) => {
      const tries = await countPinTry();
      const ok = await checkPin(pin);
      if (ok) await resetPinTries();
      return { ok, tries };
    },
    networkMode: 'always',
    onSuccess: ({ ok, tries }) => {
      if (ok) { unlock(); return; }
      form.reset();
      if (pinTriesLeft(tries) === 0) signOut(); // ADR-011 §3: the failed-PIN limit wipes everything
      else setWrong(w => Math.max(w, tries));
    },
  });
  const biometricFailed = attempt.data === false;
  return {
    name: session.staff.name,
    initials: session.staff.initials,
    biometric,
    unlockMethod,
    biometricFailed,
    showPin: offersPin(biometric, biometricFailed),
    pinForm: form,
    triesLeft: wrong > 0 ? pinTriesLeft(wrong) : null,
    error: check.error ? errorKey(check.error) : null,
    busy: attempt.isPending || check.isPending,
    onUnlock: () => attempt.mutate(),
    onSubmitPin: () => void form.handleSubmit(v => check.mutate(v.pin))(),
    onSwitchUser: signOut,
  };
}
