import type { ReactNode } from 'react';
import { useWatch } from 'react-hook-form';
import { Info } from 'lucide-react';
import { useI18n, type Country } from '@medvya/i18n';
import { templateIdOf, type Rules } from '../domain/onboarding';
import { Field, FieldError, SelectField, TextField, type FormProps } from './Fields';

const grid = 'grid gap-4 md:grid-cols-2';

type StepProps = FormProps & { country: Country; rules: Rules };

function Group({ legend, children }: { legend: string; children: ReactNode }) {
  return (
    <fieldset className="flex flex-col gap-3">
      <legend className="mb-1 text-sm font-bold">{legend}</legend>
      {children}
    </fieldset>
  );
}

/** Step 1. The mode decides the path: an owner who is not a doctor skips the licence step. */
export function ProfileStep({ form }: FormProps) {
  const { t } = useI18n();
  const mode = useWatch({ control: form.control, name: 'mode' });
  return (
    <div className="flex flex-col gap-4">
      <Group legend={t('onboarding.mode.legend')}>
        {(['Solo', 'Clinic'] as const).map(m => (
          <label key={m} className="flex min-h-11 items-center gap-2.5 text-sm">
            <input type="radio" value={m} {...form.register('mode')} className="size-4 accent-brand-700" />
            {t(`onboarding.mode.${m}`)}
          </label>
        ))}
      </Group>
      <div className={grid}>
        <TextField form={form} name="fullName" label={t('onboarding.field.fullName')} required />
        <TextField form={form} name="specialty" label={t('onboarding.field.specialty')} required={mode === 'Solo'} optional={mode === 'Clinic'} />
        <TextField form={form} name="qualifications" label={t('onboarding.field.qualifications')} optional />
        <TextField form={form} name="languages" label={t('onboarding.field.languages')} optional hint={t('onboarding.hint.languages')} />
      </div>
    </div>
  );
}

/** Step 2 (doctors only): council or authority, number, certificate, and the "Verification pending" notice. */
export function LicenceStep({ form, country, rules }: StepProps) {
  const { t } = useI18n();
  const certificate = useWatch({ control: form.control, name: 'certificateName' });
  return (
    <div className="flex flex-col gap-4">
      <div className={grid}>
        <SelectField form={form} name="authority" label={t(`onboarding.field.authority.${country}`)} required placeholder={t('onboarding.choose')}
          options={rules.authorities.map(a => a.code)} optionLabel={code => `${code} · ${rules.authorities.find(a => a.code === code)?.name ?? ''}`} />
        <TextField form={form} name="number" label={t(`onboarding.field.licenceNumber.${country}`)} required ltr hint={t(`onboarding.hint.licenceNumber.${country}`)} />
        <Field id="certificate" label={t('onboarding.field.certificate')} optional wide hint={t('onboarding.hint.certificate')}
          error={form.formState.errors.certificateName?.message}>
          {a => <input {...a} type="file" accept="application/pdf,image/*" className="min-h-11 text-sm file:me-3 file:rounded-md file:border file:bg-muted file:px-3 file:py-2"
            onChange={e => form.setValue('certificateName', e.target.files?.[0]?.name ?? '', { shouldDirty: true })} />}
        </Field>
        {certificate && <p className="text-sm md:col-span-2">{t('onboarding.certificate.chosen', { name: certificate })}</p>}
      </div>
      <p className="flex items-start gap-2 rounded-lg bg-muted p-3 text-sm" role="note">
        <Info className="mt-0.5 size-4 shrink-0" aria-hidden />{t('onboarding.licence.notice')}
      </p>
    </div>
  );
}

export function ClinicStep({ form, country }: StepProps) {
  const { t } = useI18n();
  return (
    <div className={grid}>
      <TextField form={form} name="clinicName" label={t('onboarding.field.clinicName')} required />
      <TextField form={form} name="address" label={t('onboarding.field.address')} required />
      <TextField form={form} name="registrationNumber" label={t(`onboarding.field.registration.${country}`)} optional ltr />
      <TextField form={form} name="taxId" label={t(`onboarding.field.taxId.${country}`)} optional ltr hint={t(`onboarding.hint.taxId.${country}`)} />
    </div>
  );
}

function TimeRange({ form, from, to, label }: FormProps & { from: 'weekdayStart1' | 'weekdayStart2' | 'saturdayStart'; to: 'weekdayEnd1' | 'weekdayEnd2' | 'saturdayEnd'; label: string }) {
  const { t } = useI18n();
  return (
    <div className="flex flex-col gap-1.5">
      <p className="text-sm font-semibold">{label}</p>
      <div className="grid grid-cols-2 gap-3">
        <TextField form={form} name={from} label={t('onboarding.field.from')} type="time" ltr />
        <TextField form={form} name={to} label={t('onboarding.field.to')} type="time" ltr />
      </div>
    </div>
  );
}

export function HoursStep({ form, rules }: StepProps) {
  const { t } = useI18n();
  const weekdayError = form.formState.errors.weekdayStart1?.message;
  const saturdayError = form.formState.errors.saturdayStart?.message;
  return (
    <div className="flex flex-col gap-5">
      <Group legend={t('onboarding.hours.weekdays')}>
        <div className={grid}>
          <TimeRange form={form} from="weekdayStart1" to="weekdayEnd1" label={t('onboarding.hours.morning')} />
          <TimeRange form={form} from="weekdayStart2" to="weekdayEnd2" label={t('onboarding.hours.evening')} />
        </div>
      </Group>
      <Group legend={t('onboarding.hours.saturday')}>
        <div className={grid}><TimeRange form={form} from="saturdayStart" to="saturdayEnd" label={t('onboarding.hours.saturdayWindow')} /></div>
      </Group>
      <div className={grid}>
        <SelectField form={form} name="slotMinutes" label={t('onboarding.field.slot')} required placeholder={t('onboarding.choose')}
          options={rules.slotMinutes.map(String)} optionLabel={m => t('onboarding.slotOption', { n: m })} />
      </div>
      <div className={grid}>
        <TextField form={form} name="newVisitFee" label={t('onboarding.field.newFee', { currency: rules.currency })} required ltr inputMode="numeric" />
        <TextField form={form} name="followUpFee" label={t('onboarding.field.followUpFee', { currency: rules.currency })} required ltr inputMode="numeric"
          hint={t('onboarding.hint.fees', { currency: rules.currency })} />
      </div>
      {/* Window errors belong to a pair of fields, so they are also shown once under their group. */}
      {(weekdayError || saturdayError) && <div className="flex flex-col gap-1"><FieldError id="hours-window-error" code={weekdayError ?? saturdayError} /></div>}
    </div>
  );
}

export function TemplatesStep({ form, rules }: StepProps) {
  const { t } = useI18n();
  const error = form.formState.errors.templates?.message;
  return (
    <Group legend={t('onboarding.templates.legend')}>
      <p className="text-sm text-muted-foreground">{t('onboarding.templates.hint')}</p>
      <div className={grid}>
        {rules.templates.map(templateIdOf).filter(id => id !== undefined).map(id => (
          <label key={id} className="flex min-h-11 items-center gap-2.5 rounded-lg border p-3 text-sm">
            <input type="checkbox" value={id} {...form.register('templates')} className="size-4 accent-brand-700" />
            {t(`onboarding.template.${id}`)}
          </label>
        ))}
      </div>
      <FieldError id="templates-error" code={error} />
    </Group>
  );
}

export function TeamStep({ form }: FormProps) {
  const { t } = useI18n();
  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted-foreground">{t('onboarding.team.intro')}</p>
      <div className={grid}>
        <TextField form={form} name="receptionistEmail" label={t('onboarding.field.receptionist')} optional type="email" inputMode="email" ltr />
        <TextField form={form} name="nurseEmail" label={t('onboarding.field.nurse')} optional type="email" inputMode="email" ltr />
      </div>
    </div>
  );
}
