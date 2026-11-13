import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Input, Textarea } from '@medvya/ui';
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
  /** The save was refused because someone else changed the profile first. */
  changed: boolean;
  onReload: () => void;
  nameHasLink: boolean;
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
          <Field id="profile-name" label={t('settings.profile.name')} error={p.nameHasLink ? t('settings.error.link') : errors.name && t('settings.error.required')} className="sm:col-span-2">
            <Input id="profile-name" autoComplete="organization" {...describedBy('profile-name', !!errors.name)} {...p.form.register('name')} />
          </Field>
          <Field id="profile-address" label={t('settings.profile.address')} error={errors.address && t('settings.error.tooLong')} className="sm:col-span-2">
            <Textarea id="profile-address" rows={3} autoComplete="street-address" {...describedBy('profile-address', !!errors.address)} {...p.form.register('address')} />
          </Field>
          <Field id="profile-registration" label={t('settings.profile.registration')} hint={t('settings.profile.registrationHint')} error={errors.registrationNumber && t('settings.error.tooLong')}>
            <Input id="profile-registration" autoComplete="off" {...describedBy('profile-registration', !!errors.registrationNumber, true)} {...p.form.register('registrationNumber')} />
          </Field>
          <Field id="profile-registration-expires" label={t('settings.profile.registrationExpires')} hint={t('settings.profile.registrationExpiresHint')}>
            <Input id="profile-registration-expires" type="date" dir="ltr" {...describedBy('profile-registration-expires', false, true)} {...p.form.register('registrationExpiresOn')} />
          </Field>
          <Field id="profile-tax" label={t('settings.profile.tax')} hint={t('settings.profile.taxHint')} error={errors.taxNumber && t('settings.error.tooLong')}>
            <Input id="profile-tax" autoComplete="off" {...describedBy('profile-tax', !!errors.taxNumber, true)} {...p.form.register('taxNumber')} />
          </Field>
          <Field id="profile-phone" label={t('settings.profile.phone')} error={errors.phone && t('settings.error.phone')}>
            <Input id="profile-phone" type="tel" dir="ltr" autoComplete="tel" {...describedBy('profile-phone', !!errors.phone)} {...p.form.register('phone')} />
          </Field>
          <Field id="profile-email" label={t('settings.profile.email')} error={errors.email && t('settings.error.email')}>
            <Input id="profile-email" type="email" dir="ltr" autoComplete="email" {...describedBy('profile-email', !!errors.email)} {...p.form.register('email')} />
          </Field>
          <div className="flex flex-wrap items-center gap-3 sm:col-span-2" aria-live="polite">
            <Button type="submit" loading={p.saving}>{t('settings.save')}</Button>
            {p.saved && <span>{t('settings.saved')}</span>}
            {p.changed && (
              <span role="alert" className={fieldError}>
                {t('settings.changed')} <Button type="button" variant="outline" onClick={p.onReload}>{t('settings.changed.reload')}</Button>
              </span>
            )}
            {p.saveFailed && !p.nameHasLink && <span role="alert" className={fieldError}>{t('settings.saveFailed')}</span>}
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
            {/* #896: the browser's own file button is in the browser's language, not the app's; a translated button stands in for it.
                A bare input: the house Input's full width outlasts sr-only, which made the page scroll sideways. */}
            <input id="logo-file" type="file" accept="image/png,image/jpeg" className="peer sr-only" disabled={p.uploading}
              {...describedBy('logo-file', !!logoError, true)} onChange={e => { p.onLogo(e.currentTarget.files?.[0]); e.currentTarget.value = ''; }} />
            <label htmlFor="logo-file" className={buttonVariants({ variant: 'outline', className: 'w-fit cursor-pointer peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-ring peer-disabled:pointer-events-none peer-disabled:opacity-50' })}>
              {t('settings.logo.pick')}
            </label>
          </Field>
          {p.hasLogo && <Button type="button" variant="outline" loading={p.removing} onClick={p.onRemoveLogo}>{t('settings.logo.remove')}</Button>}
        </div>
        {p.uploading && <p role="status">{t('settings.logo.uploading')}</p>}
      </Section>
    </div>
  );
}
