import type { FormEventHandler } from 'react';
import { Controller, type FieldValues, type UseFormReturn } from 'react-hook-form';
import { Info } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import { doctorSteps, type Authority, type FieldProblem, type DoctorForm, type DoctorStep, type LicenceForm } from '../domain/doctors';
import type { Location } from '../domain/settings';
import { describedBy, fieldError } from './fieldStyles';
import { Field, Section } from './parts';

type Errors = Record<string, FieldProblem | undefined>;

/** The text under an invalid field: what the server said (required, format, past, …) or "invalid" for what the form itself refused. */
function useFieldError(errors: Errors) {
  const { t } = useI18n();
  return (name: string) => (errors[name] ? t(`doctors.error.${errors[name]!}`) : null);
}

/** Authority, number, category, expiry and certificate file name: the licence step, also used to correct a rejected licence. */
export function LicenceFields<T extends FieldValues>({ form, errors, authorities, prefix }: { form: UseFormReturn<T>; errors: Errors; authorities: Authority[]; prefix: string }) {
  const { t, country } = useI18n();
  const error = useFieldError(errors);
  const f = form as unknown as UseFormReturn<LicenceForm>;
  return (
    <>
      <Field id={`${prefix}-authority`} label={t(`onboarding.field.authority.${country}`)} error={error('authority')}>
        <Controller control={f.control} name="authority" render={({ field }) => (
          <Select value={field.value} onValueChange={field.onChange}>
            <SelectTrigger id={`${prefix}-authority`} className="h-11 w-full" {...describedBy(`${prefix}-authority`, !!errors.authority)}><SelectValue placeholder={t('doctors.choose')} /></SelectTrigger>
            <SelectContent>{authorities.map(a => <SelectItem key={a.code} value={a.code}>{a.name}</SelectItem>)}</SelectContent>
          </Select>
        )} />
      </Field>
      <Field id={`${prefix}-number`} label={t(`onboarding.field.licenceNumber.${country}`)} error={error('number')}>
        <Input id={`${prefix}-number`} dir="ltr" className="h-11" autoComplete="off" {...describedBy(`${prefix}-number`, !!errors.number)} {...f.register('number')} />
      </Field>
      <Field id={`${prefix}-category`} label={t('doctors.licence.category')} hint={t('doctors.licence.categoryHint')} error={error('category')}>
        <Input id={`${prefix}-category`} className="h-11" autoComplete="off" {...describedBy(`${prefix}-category`, !!errors.category, true)} {...f.register('category')} />
      </Field>
      <Field id={`${prefix}-expires`} label={t('doctors.licence.expires')} error={error('expiresOn')}>
        <Input id={`${prefix}-expires`} type="date" dir="ltr" className="h-11" {...describedBy(`${prefix}-expires`, !!errors.expiresOn)} {...f.register('expiresOn')} />
      </Field>
      <Field id={`${prefix}-certificate`} label={t('onboarding.field.certificate')} hint={t('doctors.licence.certificateHint')} error={error('certificateName')} className="sm:col-span-2">
        <Input id={`${prefix}-certificate`} className="h-11" autoComplete="off" {...describedBy(`${prefix}-certificate`, !!errors.certificateName, true)} {...f.register('certificateName')} />
      </Field>
    </>
  );
}

type Props = {
  form: UseFormReturn<DoctorForm>;
  fieldErrors: Errors;
  step: DoctorStep;
  stepIndex: number;
  lastStep: boolean;
  authorities: Authority[];
  locations: Location[];
  currency: string;
  submitting: boolean;
  submitFailed: boolean;
  emailFailed: boolean;
  signature: File | null;
  signatureProblem: 'type' | 'size' | null;
  onPickSignature: (file: File | undefined) => void;
  onBack: () => void;
  onNext: () => void;
  onClose: () => void;
  onSubmit: FormEventHandler<HTMLFormElement>;
};

/** Add doctor (F-09): details, licence, verification, schedule, fees, access, signature. Nothing is created until the last step. */
export default function DoctorWizard(p: Props) {
  const { t } = useI18n();
  const error = useFieldError(p.fieldErrors);
  const { register, control } = p.form;
  const title = t(`doctors.step.${p.step}`);
  return (
    <Section title={t('doctors.add.title')} intro={t('doctors.add.intro')}>
      <p aria-live="polite" className="font-bold">{t('doctors.add.progress', { n: p.stepIndex + 1, total: doctorSteps.length, title })}</p>
      <form noValidate onSubmit={p.onSubmit} className="flex flex-col gap-4">
        <div className="grid gap-4 sm:grid-cols-2">
          {p.step === 'details' && (
            <>
              <Field id="d-name" label={t('onboarding.field.fullName')} error={error('fullName')}>
                <Input id="d-name" className="h-11" autoComplete="off" {...describedBy('d-name', !!p.fieldErrors.fullName)} {...register('fullName')} />
              </Field>
              <Field id="d-email" label={t('doctors.email')} hint={t('doctors.emailHint')} error={error('email')}>
                <Input id="d-email" type="email" dir="ltr" className="h-11" autoComplete="off" {...describedBy('d-email', !!p.fieldErrors.email, true)} {...register('email')} />
              </Field>
              <Field id="d-specialty" label={t('onboarding.field.specialty')} error={error('specialty')}>
                <Input id="d-specialty" className="h-11" autoComplete="off" {...describedBy('d-specialty', !!p.fieldErrors.specialty)} {...register('specialty')} />
              </Field>
              <Field id="d-qualifications" label={t('onboarding.field.qualifications')} error={error('qualifications')}>
                <Input id="d-qualifications" className="h-11" autoComplete="off" {...describedBy('d-qualifications', !!p.fieldErrors.qualifications)} {...register('qualifications')} />
              </Field>
              <Field id="d-languages" label={t('onboarding.field.languages')} error={error('languages')} className="sm:col-span-2">
                <Input id="d-languages" className="h-11" autoComplete="off" {...describedBy('d-languages', !!p.fieldErrors.languages)} {...register('languages')} />
              </Field>
            </>
          )}
          {p.step === 'licence' && <LicenceFields form={p.form} errors={p.fieldErrors} authorities={p.authorities} prefix="d" />}
          {p.step === 'verification' && (
            <div className="flex flex-col gap-2 sm:col-span-2">
              <p><span className="font-bold">{t('doctors.verification.status')}</span> {t('doctors.licence.Pending')}</p>
              <p>{t('doctors.verification.body')}</p>
              <p>{t('doctors.verification.until')}</p>
            </div>
          )}
          {p.step === 'schedule' && <p className="sm:col-span-2">{t('doctors.schedule.body')}</p>}
          {p.step === 'fees' && (
            <>
              <Field id="d-new-fee" label={t('onboarding.field.newFee', { currency: p.currency })} hint={t('doctors.fees.hint')} error={error('newVisitFee')}>
                <Input id="d-new-fee" inputMode="decimal" dir="ltr" className="h-11" autoComplete="off" {...describedBy('d-new-fee', !!p.fieldErrors.newVisitFee, true)} {...register('newVisitFee')} />
              </Field>
              <Field id="d-follow-fee" label={t('onboarding.field.followUpFee', { currency: p.currency })} error={error('followUpFee')}>
                <Input id="d-follow-fee" inputMode="decimal" dir="ltr" className="h-11" autoComplete="off" {...describedBy('d-follow-fee', !!p.fieldErrors.followUpFee)} {...register('followUpFee')} />
              </Field>
            </>
          )}
          {p.step === 'access' && (
            <>
              <Field id="d-role" label={t('doctors.access.role')} hint={t('doctors.access.roleHint')}>
                <Input id="d-role" className="h-11" readOnly value={t('doctors.access.roleValue')} {...describedBy('d-role', false, true)} />
              </Field>
              <Field id="d-facility" label={t('doctors.access.facility')} hint={t('doctors.access.facilityHint')} error={error('facilityId')}>
                <Controller control={control} name="facilityId" render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger id="d-facility" className="h-11 w-full" {...describedBy('d-facility', !!p.fieldErrors.facilityId, true)}><SelectValue /></SelectTrigger>
                    <SelectContent>{p.locations.map(l => <SelectItem key={l.id} value={l.id}>{l.name}</SelectItem>)}</SelectContent>
                  </Select>
                )} />
              </Field>
            </>
          )}
          {p.step === 'signature' && (
            <div className="flex flex-col gap-3 sm:col-span-2">
              <p role="note" className="flex items-start gap-2 rounded-md border bg-muted p-3 text-sm font-semibold">
                <Info className="mt-0.5 size-4 shrink-0" aria-hidden />{t('doctors.signature.notice')}
              </p>
              <Field id="d-signature" label={t('doctors.signature.choose')} hint={t('doctors.signature.hint')} error={p.signatureProblem ? t(`doctors.signature.error.${p.signatureProblem}`) : null}>
                <Input id="d-signature" type="file" accept="image/png,image/jpeg" className="h-auto min-h-11 py-2" {...describedBy('d-signature', !!p.signatureProblem, true)}
                  onChange={e => p.onPickSignature(e.currentTarget.files?.[0])} />
              </Field>
              {p.signature && <p role="status">{t('doctors.signature.chosen')}</p>}
            </div>
          )}
        </div>
        <div className="flex flex-wrap items-center gap-3" aria-live="polite">
          {p.stepIndex > 0 && <Button type="button" variant="outline" className="min-h-11" onClick={p.onBack}>{t('doctors.back')}</Button>}
          {/* Distinct keys: one button must never turn into the submit button under the click that moved to the last step. */}
          {p.lastStep
            ? <Button key="submit" type="submit" className="min-h-11" loading={p.submitting}>{t('doctors.add.submit')}</Button>
            : <Button key="next" type="button" className="min-h-11" onClick={p.onNext}>{t('doctors.next')}</Button>}
          <Button type="button" variant="ghost" className="min-h-11" onClick={p.onClose}>{t('settings.cancel')}</Button>
          {p.submitFailed && <span role="alert" className={fieldError}>{t('settings.saveFailed')}</span>}
          {p.emailFailed && <span role="alert" className={fieldError}>{t('staff.error.email')}</span>}
        </div>
      </form>
    </Section>
  );
}
