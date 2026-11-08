import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useForm, useWatch } from 'react-hook-form';
import type { Country } from '@medvya/i18n';
import { emptySignUp, signUpSchema, signUpStepFields, toPrefill, type SignUpForm } from '../domain/signUp';
import { startSignUp } from '../services/onboardingApi';

/** Create account steps 1 (practice) and 2 (about you); step 3, securing the sign-in, is the provider's (MDV-P1-059). */
export function useSignUpController(initialCountry: Country) {
  const form = useForm<SignUpForm>({ resolver: zodResolver(signUpSchema), defaultValues: emptySignUp(initialCountry) });
  const [step, setStep] = useState<1 | 2>(1);
  const country = useWatch({ control: form.control, name: 'country' });
  const submit = form.handleSubmit(v => startSignUp(v.country, v.email.trim(), toPrefill(v)));
  return {
    form,
    step,
    country,
    chooseCountry: (c: Country) => form.setValue('country', c),
    next: async () => {
      if (step === 2) return submit();
      if (await form.trigger([...signUpStepFields[1]], { shouldFocus: true })) setStep(2);
    },
    back: () => setStep(1),
  };
}
