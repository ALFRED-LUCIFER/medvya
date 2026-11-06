import { useI18n } from '@medvya/i18n';
import { Button, Input, Textarea } from '@medvya/ui';
import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import type { ProfileForm } from '../domain/settings';
import { describedBy, fieldError } from './fieldStyles';
import { Field } from './Field';
import { Section } from './Section';

type Props = {
  form: UseFormReturn<ProfileForm>;
  loading: boolean;
  forbidden: boolean;
  loadFailed: boolean;
  saving: boolean;
  saved: boolean;
  saveFailed: boolean;
  onSubmit: FormEventHandler<HTMLFormElement>;
  logoUrl: string | null;
  hasLogo: boolean;
  clinicName: string;
  uploading: boolean;
  removing: boolean;
  logoError: 'type' | 'size' | 'failed' | null;
  onLogo: (file: File | undefined) => void;
  onRemoveLogo: () => void;
};

/** Profile (F-08): the details printed on prescriptions and invoices, and the logo. */
export default function ProfilePanel(p: Props) {
  const { t } = useI18n();
  const { errors } = p.form.formState;
  const logoError = p.logoError ? t(`settings.logo.error.${p.logoError}`) : null;
  if (p.loading) return <p role="status">{t('settings.loading')}</p>;
  if (p.loadFailed) return <p role="alert" className={fieldError}>{t(p.forbidden ? 'settings.forbidden' : 'settings.loadFailed')}</p>;
  return (
    <div className="flex flex-col gap-4">
      <Section title={t('settings.profile.title')} intro={t('settings.profile.intro')}>
        <form noValidate onSubmit={p.onSubmit} className="grid gap-4 sm:grid-cols-2">
          <Field id="profile-name" label={t('settings.profile.name')} error={errors.name && t('settings.error.required')} className="sm:col-span-2">
            <Input id="profile-name" className="h-11" autoComplete="organization" {...describedBy('profile-name', !!errors.name)} {...p.form.register('name')} />
          </Field>
          <Field id="profile-address" label={t('settings.profile.address')} error={errors.address && t('settings.error.tooLong')} className="sm:col-span-2">
            <Textarea id="profile-address" rows={3} autoComplete="street-address" {...describedBy('profile-address', !!errors.address)} {...p.form.register('address')} />
          </Field>
          <Field id="profile-registration" label={t('settings.profile.registration')} hint={t('settings.profile.registrationHint')} error={errors.registrationNumber && t('settings.error.tooLong')}>
            <Input id="profile-registration" className="h-11" autoComplete="off" {...describedBy('profile-registration', !!errors.registrationNumber, true)} {...p.form.register('registrationNumber')} />
          </Field>
          <Field id="profile-tax" label={t('settings.profile.tax')} hint={t('settings.profile.taxHint')} error={errors.taxNumber && t('settings.error.tooLong')}>
            <Input id="profile-tax" className="h-11" autoComplete="off" {...describedBy('profile-tax', !!errors.taxNumber, true)} {...p.form.register('taxNumber')} />
          </Field>
          <Field id="profile-phone" label={t('settings.profile.phone')} error={errors.phone && t('settings.error.phone')}>
            <Input id="profile-phone" type="tel" dir="ltr" className="h-11" autoComplete="tel" {...describedBy('profile-phone', !!errors.phone)} {...p.form.register('phone')} />
          </Field>
          <Field id="profile-email" label={t('settings.profile.email')} error={errors.email && t('settings.error.email')}>
            <Input id="profile-email" type="email" dir="ltr" className="h-11" autoComplete="email" {...describedBy('profile-email', !!errors.email)} {...p.form.register('email')} />
          </Field>
          <div className="flex flex-wrap items-center gap-3 sm:col-span-2" aria-live="polite">
            <Button type="submit" className="min-h-11" loading={p.saving}>{t('settings.save')}</Button>
            {p.saved && <span>{t('settings.saved')}</span>}
            {p.saveFailed && <span role="alert" className={fieldError}>{t('settings.saveFailed')}</span>}
          </div>
        </form>
      </Section>
      <Section title={t('settings.logo.title')} intro={t('settings.logo.intro')}>
        <div className="flex flex-wrap items-center gap-4">
          {p.logoUrl
            ? <img src={p.logoUrl} alt={t('settings.logo.alt', { name: p.clinicName })} className="max-h-24 max-w-48 rounded-md border bg-white object-contain p-2" />
            : <p className="text-muted-foreground">{t('settings.logo.none')}</p>}
        </div>
        <div className="flex flex-wrap items-end gap-3">
          <Field id="logo-file" label={t('settings.logo.choose')} hint={t('settings.logo.hint')} error={logoError} className="min-w-60">
            <Input id="logo-file" type="file" accept="image/png,image/jpeg" className="h-auto min-h-11 py-2" disabled={p.uploading}
              {...describedBy('logo-file', !!logoError, true)} onChange={e => { p.onLogo(e.currentTarget.files?.[0]); e.currentTarget.value = ''; }} />
          </Field>
          {p.hasLogo && <Button type="button" variant="outline" className="min-h-11" loading={p.removing} onClick={p.onRemoveLogo}>{t('settings.logo.remove')}</Button>}
        </div>
        {p.uploading && <p role="status">{t('settings.logo.uploading')}</p>}
      </Section>
    </div>
  );
}
