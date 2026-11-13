import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, ErrorSummary, Field, Input, Label, Textarea } from '@medvya/ui';
import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import type { ProfileForm } from '../domain/settings';
import { fieldError } from './fieldStyles';
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
  const { errors, submitCount } = p.form.formState;
  const nameError = p.nameHasLink ? t('settings.error.link') : errors.name && t('settings.error.required');
  const tooLong = t('settings.error.tooLong');
  const fields = [
    { id: 'profile-name', label: t('settings.profile.name'), message: nameError },
    { id: 'profile-address', label: t('settings.profile.address'), message: errors.address && tooLong },
    { id: 'profile-registration', label: t('settings.profile.registration'), message: errors.registrationNumber && tooLong },
    { id: 'profile-tax', label: t('settings.profile.tax'), message: errors.taxNumber && tooLong },
    { id: 'profile-phone', label: t('settings.profile.phone'), message: errors.phone && t('settings.error.phone') },
    { id: 'profile-email', label: t('settings.profile.email'), message: errors.email && t('settings.error.email') },
  ];
  const error = (id: string) => fields.find(f => f.id === id)?.message || undefined;
  const logoError = p.logoError ? t(`settings.logo.error.${p.logoError}`) : null;
  if (p.loading) return <p role="status">{t('settings.loading')}</p>;
  if (p.loadFailed) return <p role="alert" className={fieldError}>{t(p.forbidden ? 'settings.forbidden' : 'settings.loadFailed')}</p>;
  return (
    <div className="flex flex-col gap-4">
      <Section title={t('settings.profile.title')} intro={t('settings.profile.intro')}>
        <form noValidate onSubmit={p.onSubmit} className="grid gap-4 sm:grid-cols-2">
          {/* The server's refusal of a web address in the name moves focus too, once it arrives. */}
          <ErrorSummary title={t('field.errorSummary')} focusKey={`${submitCount}:${p.nameHasLink}`} className="sm:col-span-2"
            errors={fields.flatMap(f => (f.message ? [{ id: f.id, label: f.label, message: f.message }] : []))} />
          <Field id="profile-name" label={t('settings.profile.name')} required error={error('profile-name')} className="sm:col-span-2">
            <Input autoComplete="organization" {...p.form.register('name')} />
          </Field>
          <Field id="profile-address" label={t('settings.profile.address')} error={error('profile-address')} className="sm:col-span-2">
            <Textarea rows={3} autoComplete="street-address" {...p.form.register('address')} />
          </Field>
          <Field id="profile-registration" label={t('settings.profile.registration')} hint={t('settings.profile.registrationHint')} error={error('profile-registration')}>
            <Input autoComplete="off" {...p.form.register('registrationNumber')} />
          </Field>
          <Field id="profile-registration-expires" label={t('settings.profile.registrationExpires')} hint={t('settings.profile.registrationExpiresHint')}>
            <Input type="date" dir="ltr" {...p.form.register('registrationExpiresOn')} />
          </Field>
          <Field id="profile-tax" label={t('settings.profile.tax')} hint={t('settings.profile.taxHint')} error={error('profile-tax')}>
            <Input autoComplete="off" {...p.form.register('taxNumber')} />
          </Field>
          <Field id="profile-phone" label={t('settings.profile.phone')} error={error('profile-phone')}>
            <Input type="tel" dir="ltr" autoComplete="tel" {...p.form.register('phone')} />
          </Field>
          <Field id="profile-email" label={t('settings.profile.email')} error={error('profile-email')}>
            <Input type="email" dir="ltr" autoComplete="email" {...p.form.register('email')} />
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
          {/* Not a Field: the visible control is a label styled as a button, which must follow the hidden input as its peer. */}
          <div className="flex min-w-60 flex-col gap-1.5">
            <Label htmlFor="logo-file" className="text-label">{t('settings.logo.choose')}</Label>
            <p id="logo-file-hint" className="text-caption text-muted-foreground">{t('settings.logo.hint')}</p>
            {/* #896: the browser's own file button is in the browser's language, not the app's; a translated button stands in for it.
                A bare input: the house Input's full width outlasts sr-only, which made the page scroll sideways. */}
            <input id="logo-file" type="file" accept="image/png,image/jpeg" className="peer sr-only" disabled={p.uploading}
              aria-invalid={logoError ? true : undefined} aria-describedby={logoError ? 'logo-file-hint logo-file-error' : 'logo-file-hint'}
              onChange={e => { p.onLogo(e.currentTarget.files?.[0]); e.currentTarget.value = ''; }} />
            <label htmlFor="logo-file" className={buttonVariants({ variant: 'outline', className: 'w-fit cursor-pointer peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-ring peer-disabled:pointer-events-none peer-disabled:opacity-50' })}>
              {t('settings.logo.pick')}
            </label>
            {logoError && <p id="logo-file-error" role="alert" className="text-label text-danger-text">{logoError}</p>}
          </div>
          {p.hasLogo && <Button type="button" variant="outline" loading={p.removing} onClick={p.onRemoveLogo}>{t('settings.logo.remove')}</Button>}
        </div>
        {p.uploading && <p role="status">{t('settings.logo.uploading')}</p>}
      </Section>
    </div>
  );
}
