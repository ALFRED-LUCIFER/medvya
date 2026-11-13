import type { FormEventHandler } from 'react';
import { Controller, type FieldValues, type UseFormReturn } from 'react-hook-form';
import { Info } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, ErrorSummary, Field, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@medvya/ui';
import { doctorSteps, stepFields, type Authority, type FieldProblem, type DoctorForm, type DoctorStep, type LicenceForm } from '../domain/doctors';
import type { Location } from '../domain/settings';
import { fieldError } from './fieldStyles';
import { Section } from './Section';

type Errors = Record<string, FieldProblem | undefined>;
type Labels = Partial<Record<keyof DoctorForm, readonly [id: string, label: string]>>;

/** Each field's control id and label: the fields use them, and the error summary links to them. */
function useLabels(prefix: string, currency = ''): Labels {
  const { t, country } = useI18n();
  return {
    fullName: [`${prefix}-name`, t('onboarding.field.fullName')],
    email: [`${prefix}-email`, t('doctors.email')],
    specialty: [`${prefix}-specialty`, t('onboarding.field.specialty')],
    qualifications: [`${prefix}-qualifications`, t('onboarding.field.qualifications')],
    languages: [`${prefix}-languages`, t('onboarding.field.languages')],
    authority: [`${prefix}-authority`, t(`onboarding.field.authority.${country}`)],
    number: [`${prefix}-number`, t(`onboarding.field.licenceNumber.${country}`)],
    category: [`${prefix}-category`, t('doctors.licence.category')],
    expiresOn: [`${prefix}-expires`, t('doctors.licence.expires')],
    certificateName: [`${prefix}-certificate`, t('onboarding.field.certificate')],
    attested: [`${prefix}-attested`, t('doctors.licence.attest')],
    newVisitFee: [`${prefix}-new-fee`, t('onboarding.field.newFee', { currency })],
    followUpFee: [`${prefix}-follow-fee`, t('onboarding.field.followUpFee', { currency })],
    facilityId: [`${prefix}-facility`, t('doctors.access.facility')],
  };
}

/** The text under an invalid field: what the server said (required, format, past, …) or "invalid" for what the form itself refused. */
function useFieldError(errors: Errors) {
  const { t } = useI18n();
  return (name: string) => (errors[name] ? t(`doctors.error.${errors[name]!}`) : undefined);
}

/** After a failed Next or save: this step's fields in error, as links; it takes focus when focusKey changes. */
export function StepErrorSummary({ errors, fields, prefix, currency, focusKey }: {
  errors: Errors; fields: readonly (keyof DoctorForm)[]; prefix: string; currency?: string; focusKey: number;
}) {
  const { t } = useI18n();
  const error = useFieldError(errors);
  const labels = useLabels(prefix, currency);
  const list = fields.flatMap(name => {
    const message = error(name), at = labels[name];
    return message && at ? [{ id: at[0], label: at[1], message }] : [];
  });
  return <ErrorSummary title={t('field.errorSummary')} focusKey={focusKey} errors={list} className="sm:col-span-2" />;
}

/** Authority, number, category, expiry, certificate file name and the owner's attestation: the licence step, also used to renew a licence. */
export function LicenceFields<T extends FieldValues>({ form, errors, authorities, prefix }: { form: UseFormReturn<T>; errors: Errors; authorities: Authority[]; prefix: string }) {
  const { t } = useI18n();
  const error = useFieldError(errors);
  const l = useLabels(prefix);
  const f = form as unknown as UseFormReturn<LicenceForm>;
  return (
    <>
      <Controller control={f.control} name="authority" render={({ field }) => (
        <Select value={field.value} onValueChange={field.onChange}>
          <Field id={l.authority![0]} label={l.authority![1]} required error={error('authority')}>
            <SelectTrigger className="w-full"><SelectValue placeholder={t('doctors.choose')} /></SelectTrigger>
          </Field>
          <SelectContent>{authorities.map(a => <SelectItem key={a.code} value={a.code}>{a.name}</SelectItem>)}</SelectContent>
        </Select>
      )} />
      <Field id={l.number![0]} label={l.number![1]} required error={error('number')}>
        <Input dir="ltr" autoComplete="off" {...f.register('number')} />
      </Field>
      <Field id={l.category![0]} label={l.category![1]} required hint={t('doctors.licence.categoryHint')} error={error('category')}>
        <Input autoComplete="off" {...f.register('category')} />
      </Field>
      <Field id={l.expiresOn![0]} label={l.expiresOn![1]} required error={error('expiresOn')}>
        <Input type="date" dir="ltr" {...f.register('expiresOn')} />
      </Field>
      <Field id={l.certificateName![0]} label={l.certificateName![1]} hint={t('doctors.licence.certificateHint')} error={error('certificateName')} className="sm:col-span-2">
        <Input autoComplete="off" {...f.register('certificateName')} />
      </Field>
      <div className="flex flex-col gap-2 sm:col-span-2">
        <label className="flex min-h-11 items-start gap-3 text-sm font-semibold">
          <input id={l.attested![0]} type="checkbox" className="mt-1 size-4 accent-brand-700" aria-invalid={errors.attested ? true : undefined}
            aria-describedby={errors.attested ? `${l.attested![0]}-error` : undefined} {...f.register('attested')} />
          {t('doctors.licence.attest')}
        </label>
        <p className="text-sm">{t('doctors.licence.reminder')}</p>
        {error('attested') && <p id={`${l.attested![0]}-error`} role="alert" className={fieldError}>{error('attested')}</p>}
      </div>
    </>
  );
}

type Props = {
  form: UseFormReturn<DoctorForm>;
  fieldErrors: Errors;
  /** Changes once per failed Next or save: the error summary takes focus then. */
  errorFocusKey: number;
  step: DoctorStep;
  stepIndex: number;
  lastStep: boolean;
  authorities: Authority[];
  locations: Location[];
  currency: string;
  submitting: boolean;
  submitFailed: boolean;
  emailFailed: boolean;
  planFull: boolean;
  signature: File | null;
  signatureProblem: 'type' | 'size' | null;
  onPickSignature: (file: File | undefined) => void;
  onBack: () => void;
  onNext: () => void;
  onClose: () => void;
  onSubmit: FormEventHandler<HTMLFormElement>;
};

/** Add doctor (F-09): details, licence, schedule, fees, access, signature. Nothing is created until the last step. */
export default function DoctorWizard(p: Props) {
  const { t } = useI18n();
  const error = useFieldError(p.fieldErrors);
  const l = useLabels('d', p.currency);
  const { register, control, watch } = p.form;
  const title = t(`doctors.step.${p.step}`);
  return (
    <Section title={t('doctors.add.title')} intro={t('doctors.add.intro')}>
      <p aria-live="polite" className="font-bold">{t('doctors.add.progress', { n: p.stepIndex + 1, total: doctorSteps.length, title })}</p>
      <form noValidate onSubmit={p.onSubmit} className="flex flex-col gap-4">
        <div className="grid gap-4 sm:grid-cols-2">
          <StepErrorSummary errors={p.fieldErrors} fields={stepFields[p.step]} prefix="d" currency={p.currency} focusKey={p.errorFocusKey} />
          {p.step === 'details' && (
            <>
              <Field id={l.fullName![0]} label={l.fullName![1]} required error={error('fullName')}>
                <Input autoComplete="off" {...register('fullName')} />
              </Field>
              <Field id={l.email![0]} label={l.email![1]} required hint={t('doctors.emailHint')} error={error('email')}>
                <Input type="email" dir="ltr" autoComplete="off" {...register('email')} />
              </Field>
              <Field id={l.specialty![0]} label={l.specialty![1]} required error={error('specialty')}>
                <Input autoComplete="off" {...register('specialty')} />
              </Field>
              <Field id={l.qualifications![0]} label={l.qualifications![1]} error={error('qualifications')}>
                <Input autoComplete="off" {...register('qualifications')} />
              </Field>
              <Field id={l.languages![0]} label={l.languages![1]} error={error('languages')} className="sm:col-span-2">
                <Input autoComplete="off" {...register('languages')} />
              </Field>
            </>
          )}
          {p.step === 'licence' && <LicenceFields form={p.form} errors={p.fieldErrors} authorities={p.authorities} prefix="d" />}
          {p.step === 'schedule' && <p className="sm:col-span-2">{t('doctors.schedule.body')}</p>}
          {p.step === 'fees' && (
            <>
              <Field id={l.newVisitFee![0]} label={l.newVisitFee![1]} required hint={t('doctors.fees.hint')} error={error('newVisitFee')}>
                <Input inputMode="decimal" dir="ltr" autoComplete="off" {...register('newVisitFee')} />
              </Field>
              <Field id={l.followUpFee![0]} label={l.followUpFee![1]} required error={error('followUpFee')}>
                <Input inputMode="decimal" dir="ltr" autoComplete="off" {...register('followUpFee')} />
              </Field>
            </>
          )}
          {p.step === 'access' && (
            <>
              <Field id="d-role" label={t('doctors.access.role')} hint={t('doctors.access.roleHint')}>
                <Input readOnly value={t('doctors.access.roleValue')} />
              </Field>
              <Controller control={control} name="facilityId" render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <Field id={l.facilityId![0]} label={l.facilityId![1]} required hint={t('doctors.access.facilityHint')} error={error('facilityId')}>
                    <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
                  </Field>
                  <SelectContent>{p.locations.map(loc => <SelectItem key={loc.id} value={loc.id}>{loc.name}</SelectItem>)}</SelectContent>
                </Select>
              )} />
              {p.locations.length > 1 && (
                <Controller control={control} name="alsoFacilityIds" render={({ field }) => (
                  <fieldset className="flex flex-col gap-2 sm:col-span-2">
                    <legend className="mb-1 font-bold">{t('staff.alsoAt')}</legend>
                    {p.locations.filter(l => l.id !== watch('facilityId')).map(l => (
                      <label key={l.id} className="flex min-h-11 cursor-pointer items-center gap-3 rounded-lg border p-3 has-[:checked]:border-primary has-[:focus-visible]:ring-[3px] has-[:focus-visible]:ring-ring/50">
                        <input type="checkbox" className="size-4 accent-brand-700" checked={field.value.includes(l.id)}
                          onChange={() => field.onChange(field.value.includes(l.id) ? field.value.filter(x => x !== l.id) : [...field.value, l.id])} />
                        <span className="font-semibold">{l.name}</span>
                      </label>
                    ))}
                    <p className="text-sm text-muted-foreground">{t('doctors.alsoAtHint')}</p>
                  </fieldset>
                )} />
              )}
            </>
          )}
          {p.step === 'signature' && (
            <div className="flex flex-col gap-3 sm:col-span-2">
              <p role="note" className="flex items-start gap-2 rounded-md border bg-muted p-3 text-sm font-semibold">
                <Info className="mt-0.5 size-4 shrink-0" aria-hidden />{t('doctors.signature.notice')}
              </p>
              <Field id="d-signature" label={t('doctors.signature.choose')} hint={t('doctors.signature.hint')} error={p.signatureProblem ? t(`doctors.signature.error.${p.signatureProblem}`) : undefined}>
                <Input type="file" accept="image/png,image/jpeg" className="h-auto py-2" onChange={e => p.onPickSignature(e.currentTarget.files?.[0])} />
              </Field>
              {p.signature && <p role="status">{t('doctors.signature.chosen')}</p>}
            </div>
          )}
        </div>
        <div className="flex flex-wrap items-center gap-3" aria-live="polite">
          {p.stepIndex > 0 && <Button type="button" variant="outline" onClick={p.onBack}>{t('doctors.back')}</Button>}
          {/* Distinct keys: one button must never turn into the submit button under the click that moved to the last step. */}
          {p.lastStep
            ? <Button key="submit" type="submit" loading={p.submitting}>{t('doctors.add.submit')}</Button>
            : <Button key="next" type="button" onClick={p.onNext}>{t('doctors.next')}</Button>}
          <Button type="button" variant="ghost" onClick={p.onClose}>{t('settings.cancel')}</Button>
          {p.submitFailed && <span role="alert" className={fieldError}>{t('settings.saveFailed')}</span>}
          {p.emailFailed && <span role="alert" className={fieldError}>{t('staff.error.email')}</span>}
          {p.planFull && <span role="alert" className={fieldError}>{t('plan.full')}</span>}
        </div>
      </form>
    </Section>
  );
}
